using System;

namespace EPiServer.Amazon.Events
{
    internal static class EpochDateTimeConverter
    {
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static bool TryParse(string s, out DateTime date)
        {
            double ms;
            if (double.TryParse(s, out ms))
            {
                date = Epoch.AddMilliseconds(ms);
                return true;
            }

            date = DateTime.MinValue;
            return false;
        }
    }
}
