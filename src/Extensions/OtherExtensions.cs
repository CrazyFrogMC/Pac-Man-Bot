﻿using System;
using Microsoft.Extensions.DependencyInjection;
using PacManBot.Constants;

namespace PacManBot.Extensions
{
    public static class OtherExtensions
    {
        public static T Get<T>(this IServiceProvider provider) // I thought the long name was ugly
        {
            return provider.GetRequiredService<T>();
        }


        public static int Ceiling(this double num)
        {
            return (int)Math.Ceiling(num);
        }


        public static string Humanized(this TimeSpan span)
        {
            int days = (int)span.TotalDays, hours = span.Hours, minutes = span.Minutes;

            string result = $"{days} day{"s".If(days > 1)}, ".If(days > 0)
                          + $"{hours} hour{"s".If(hours > 1)}, ".If(hours > 0)
                          + $"{minutes} minute{"s".If(minutes > 1)}".If(minutes > 0);

            return result != "" ? result : "Just now";
        }


        public static string Humanized(this TimePeriod period)
        {
            switch (period)
            {
                case TimePeriod.Month: return "in the last 30 days";
                case TimePeriod.Week: return "in the last 7 days";
                case TimePeriod.Day: return "in the last 24 hours";
                default: return "of all time";
            }
        }
    }
}
