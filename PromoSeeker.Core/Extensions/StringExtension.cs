﻿using System;

namespace PromoSeeker.Core
{
    public static class StringExtension
    {
        /// <summary>
        /// Enhanced version of <see cref="string.Contains(string)"/> to support comparisons.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="value"></param>
        /// <param name="comparsionType"></param>
        /// <returns></returns>
        public static bool ContainsEx(this string source, string value, StringComparison comparsionType = StringComparison.OrdinalIgnoreCase)
        {
            return source?.IndexOf(value, comparsionType) >= 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="value"></param>
        /// <param name="comparsionType"></param>
        /// <returns></returns>
        public static bool ContainsAny(this string source, string[] values, StringComparison comparsionType = StringComparison.OrdinalIgnoreCase)
        {
            foreach (var value in values)
            {
                if (source.ContainsEx(value, comparsionType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
