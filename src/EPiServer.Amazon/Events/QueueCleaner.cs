using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using EPiServer.Logging;

namespace EPiServer.Amazon.Events
{
    internal class QueueCleaner : IDisposable
    {
#if DEBUG
        public static readonly TimeSpan DeleteCheckTimeout = TimeSpan.FromMinutes(1);
#else
        public static readonly TimeSpan DeleteCheckTimeout = TimeSpan.FromMinutes(30);
#endif


        private static object _syncLock = new object();
        private static readonly CancellationTokenSource _appDomainUnload = new CancellationTokenSource();
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        private readonly IAmazonSimpleNotificationService _notificationClient;
        private readonly IAmazonSQS _queueService;

        const string SentTimestamp = "SentTimestamp";
        const int Retries = 3;
        
        private string _ownQueueUrl;
        private TimeSpan _deleteQueueLimit;
        private string _queueNamePrefix;
        private Timer _interval;

        public QueueCleaner(IAmazonSQS queueService, IAmazonSimpleNotificationService notificationClient, string queueNamePrefix, string ownQueueUrl, TimeSpan queueExpiration, TimeSpan deleteQueueLimit)
        {
            AppDomain.CurrentDomain.DomainUnload += (object sender, EventArgs e) => _appDomainUnload.Cancel();

            _notificationClient = notificationClient;
            _queueService = queueService;
            _queueNamePrefix = queueNamePrefix;
            _ownQueueUrl = ownQueueUrl;
            _deleteQueueLimit = deleteQueueLimit;
        }

        public void Run()
        {

#if DEBUG
            _interval = new Timer(SafeTimerCallback, null, TimeSpan.FromSeconds(new Random().Next(60, 120)), DeleteCheckTimeout);
#else
            _interval = new Timer(SafeTimerCallback, null, TimeSpan.FromSeconds(new Random().Next(300, 600)), DeleteCheckTimeout);
#endif

        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        private void SafeTimerCallback(object state)
        {
            try
            {
                if (_appDomainUnload.IsCancellationRequested)
                {
                    if (_interval != null) _interval.Dispose();
                    return;
                }

                Process();
            
            }
            catch (Exception e)
            {
                Logger.Error("SafeTimerCallback (Process) failed", e);
            }
        }

        private void Process()
        {
            if (Monitor.TryEnter(_syncLock))
            {
                try
                {
                    Logger.Trace("Processing Cleanup ownQueue = '{0}', QueueNamePrefix = '{1}'", _ownQueueUrl, _queueNamePrefix);
                   
                    Cleanup();

                    Logger.Trace("Processed Cleanup ownQueue = '{0}', QueueNamePrefix = '{1}'", _ownQueueUrl, _queueNamePrefix);
                }
                finally
                {
                    Monitor.Exit(_syncLock);
                }
            }
        }

        /// <summary>
        /// List all queues and find cleanup unused queues.
        /// </summary>
        /// <returns></returns>
        internal IEnumerable<QueueInfo> Cleanup()
        {
            var  queueInfos = ListQueues();
            Log("Queues before deleting Unused queues", queueInfos);

            queueInfos = DeleteUnusedQueues(queueInfos);
            Log("Queues after deleting Unused queues", queueInfos);
            
            return queueInfos;
        }

        /// <summary>
        /// List queues and filter own queue.
        /// </summary>
        /// <returns></returns>
        internal IEnumerable<QueueInfo> ListQueues()
        {
            var queueUrls = Enumerable.Empty<string>();
            ExecuteQueueQuery(() =>
            {
                var queues = _queueService.ListQueuesAsync(new ListQueuesRequest(_queueNamePrefix), new CancellationToken()).GetAwaiter().GetResult();
                queueUrls = queues.QueueUrls.Where(q => !q.Equals(_ownQueueUrl, StringComparison.OrdinalIgnoreCase));
            });

            return GetQueuesInfo(queueUrls);
        }

        private IList<QueueInfo> GetQueuesInfo(IEnumerable<string> queueUrls)
        {
            IList<QueueInfo> queueInfos = new List<QueueInfo>();
            foreach (var qeueuUrl in queueUrls)
            {
                QueueInfo queuInfo = new QueueInfo(qeueuUrl);

                string queueArn;
                TryGetQueueArn(qeueuUrl, out queueArn);
                queuInfo.QueueArn = queueArn;

                Subscription sub;
                TryGetSubscription(queueArn, out sub);
                queuInfo.Subscription = sub;

                queueInfos.Add(queuInfo);

            }
            return queueInfos;
        }

        private IEnumerable<QueueInfo> DeleteUnusedQueues(IEnumerable<QueueInfo> queueInfos)
        {
            var unusedQueues = new List<QueueInfo>();
            foreach (var queueInfo in queueInfos)
            {
                if (IsUnusedQueue(queueInfo))
                {
                    Unsubscribe(queueInfo);

                    DeleteQueue(queueInfo);

                    unusedQueues.Add(queueInfo);
                }
            }
            return unusedQueues;
        }

        private bool IsUnusedQueue(QueueInfo queueInfo)
        {
            ReceiveMessageResponse receiveMessageResponse = null;
            var res = ExecuteQueueQuery(() =>
            {
                var receiveMessageRequest = new ReceiveMessageRequest(queueInfo.QueueUrl) { VisibilityTimeout = 0 };
                receiveMessageRequest.AttributeNames.Add(SentTimestamp);
                receiveMessageResponse = _queueService.ReceiveMessageAsync(receiveMessageRequest, new CancellationToken()).GetAwaiter().GetResult();
            }, queueInfo.QueueUrl, "Peek Message", true);


            DateTime timestamp = DateTime.MinValue;
            if (res && receiveMessageResponse.Messages != null)
            {
                var msg = receiveMessageResponse.Messages.FirstOrDefault();
                if (msg != null &&  msg.Attributes != null)
                {
                    var sentAttr = msg.Attributes.Where(a => a.Key == SentTimestamp).FirstOrDefault();
                    if (!EpochDateTimeConverter.TryParse(sentAttr.Value, out timestamp))
                    {
                        Logger.Debug("Unable to parse Epoch timestamp to DateTime '{0}'.", sentAttr.Value);
                    }
                }
            }
            
            // If the message is less than 1 hour then the queue is candidate for deleting
            var oldmsg = timestamp != DateTime.MinValue && timestamp.Add(_deleteQueueLimit) <= DateTime.UtcNow;
            if (oldmsg) Logger.Trace("Found an old message for '{0}' '{1}'", queueInfo.QueueUrl, timestamp);

            return oldmsg;
        }

        private bool TryGetSubscription(string queueArn, out Subscription subscription)
        {
            Subscription localSubscription = subscription = null;
            var res = ExecuteQueueQuery(() =>
            {
                string nextToken = null;
                while (localSubscription == null)
                {
                    var subs = _notificationClient.ListSubscriptionsAsync(new ListSubscriptionsRequest(nextToken)).GetAwaiter().GetResult();
                    localSubscription = subs.Subscriptions.Where(s => s.Endpoint == queueArn).FirstOrDefault();
                    nextToken = subs.NextToken;
                    if (subs.Subscriptions.Count() == 0 || string.IsNullOrEmpty(nextToken)) break;
                }
            }, queueArn, "GetSubscription", true);

            subscription = localSubscription;

            if (localSubscription != null) Logger.Debug("Found Subscription '{0}' for Queue '{1}'", subscription.SubscriptionArn, queueArn);

            return res && subscription != null;
        }

        private void Unsubscribe(QueueInfo queueInfo)
        {
            if (!string.IsNullOrWhiteSpace(queueInfo.QueueArn) && queueInfo.Subscription != null)
            {
                Unsubscribe(queueInfo.QueueArn, queueInfo.Subscription);
            }
        }

        private bool Unsubscribe(string queueUrl, Subscription theSub)
        {
            var res = ExecuteQueueQuery(() =>
            {
                _notificationClient.UnsubscribeAsync(new UnsubscribeRequest { SubscriptionArn = theSub.SubscriptionArn }).GetAwaiter().GetResult();
            }, queueUrl, "Unsubscribe", true);

            if(res)   
                Log(" Succeeded Unsubscribe", queueUrl);
            else
                Log(" Failed Unsubscribe", queueUrl);

            return res;
        }

        private bool TryGetQueueArn(string queueUrl, out string queueArn)
        {
            string localQueueArn = queueArn = null;
            var res = ExecuteQueueQuery(() =>
                {
                    var getAttributesRequest = new GetQueueAttributesRequest { QueueUrl = queueUrl };
                    getAttributesRequest.AttributeNames.Add("QueueArn");
                    localQueueArn = _queueService.GetQueueAttributesAsync(getAttributesRequest, new CancellationToken()).GetAwaiter().GetResult().QueueARN;
                }, queueUrl, "Get QueueArn", true);

            queueArn = localQueueArn;
            return res && queueArn != null;
        }

        private bool DeleteQueue(QueueInfo queueInfo)
        {
            return ExecuteQueueQuery(() =>
            {
                _queueService.DeleteQueueAsync(new DeleteQueueRequest() { QueueUrl = queueInfo.QueueUrl}, new CancellationToken()).GetAwaiter().GetResult();
            }, queueInfo.QueueUrl, "Delete Queue", true);
        }

        private bool ExecuteQueueQuery(Action a, string queueUrl = null, string operationName = null, bool doLog = false)
        {
            int i = 0;
            while(true)
            {
                try
                {
                    if (doLog) Log(queueUrl, string.Format(System.Globalization.CultureInfo.InvariantCulture, "Processing {0}", operationName));

                    a();
                    
                    if (doLog) Log(queueUrl, string.Format(System.Globalization.CultureInfo.InvariantCulture, "succeeded {0}", operationName));

                    return true;
                }
                catch (AmazonServiceException amazonEx)
                {
                    if (amazonEx.StatusCode == System.Net.HttpStatusCode.BadRequest || amazonEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        if (++i < Retries)
                        {
                            Thread.Sleep(2000);
                            continue;
                        }
                        if (Logger.IsWarningEnabled()) Logger.Warning(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Failed to Execute '{0}'", a.Method.Name), amazonEx);
                        return false;
                    }
                    throw;
                }
            }
        }

        private void Log(string header, IEnumerable<QueueInfo> queueInfos)
        {
            if (Logger.IsDebugEnabled())
            {
                Logger.Debug(header);
                foreach (var queueInfo in queueInfos)
                {
                    Logger.Debug("QueueUrl = '{0}', QueueArn = '{1}', SubscriptionArn = '{2}'", queueInfo.QueueUrl, queueInfo.QueueArn, queueInfo.Subscription != null ? queueInfo.Subscription.SubscriptionArn : "Missing SubscriptionArn");
                }
            }
        }

        private void Log(string queueUrl, string msg = null)
        {
            if(string.IsNullOrEmpty(msg))
                Logger.Trace("Queue '{0}'", queueUrl);
            else
                Logger.Trace("'{0}' for  Queue '{1}'", msg, queueUrl);
        }

        #region Dispose

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_interval != null)
                {
                    _interval.Dispose();
                    _interval = null;
                }
            }
        }

        #endregion
    }

    class QueueInfo
    {
        public QueueInfo(string queueUrl)
        {
            QueueUrl = queueUrl;
        }
        public string QueueUrl { get; protected set; }
        public string QueueArn { get; set; }
        public Subscription Subscription { get; set; }
    }
}
