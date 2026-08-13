using System;

namespace EPiServer.Amazon.Events
{
    public enum TestStateMessageType
    {
        None = 0,
        Awesome = 1
    }

    public class TestStateMessage
    {
        public TestStateMessage()
        {
        }

        public TestStateMessage(string serverName, string applicationName, TestStateMessageType type, DateTime sent)
        {
            ServerName = serverName;
            ApplicationName = applicationName;
            Type = type;
            Sent = sent;
        }

        public string ServerName { get; set; }

        public string ApplicationName { get; set; }

        public DateTime Sent { get; set; }

        public TestStateMessageType Type { get; set; }
    }
}
