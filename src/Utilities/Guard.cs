using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Provides guard clauses for validating method arguments and state.
    /// Centralizes common validation patterns to reduce code duplication.
    /// </summary>
    public static class Guard
    {
        /// <summary>
        /// Ensures that the specified argument is not null.
        /// </summary>
        /// <typeparam name="T">The type of the argument.</typeparam>
        /// <param name="value">The argument value to validate.</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The non-null value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T NotNull<T>(T value, [CallerArgumentExpression("value")] string paramName = null) where T : class
        {
            if (value is null)
            {
                throw new ArgumentNullException(paramName);
            }
            return value;
        }

        /// <summary>
        /// Ensures that the specified string is not null or empty.
        /// </summary>
        /// <param name="value">The string value to validate.</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The non-empty string value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        /// <exception cref="ArgumentException">Thrown when value is empty.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string NotNullOrEmpty(string value, [CallerArgumentExpression("value")] string paramName = null)
        {
            if (value is null)
            {
                throw new ArgumentNullException(paramName);
            }
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException($"String parameter '{paramName}' cannot be empty.", paramName);
            }
            return value;
        }

        /// <summary>
        /// Ensures that the specified string is not null, empty, or whitespace.
        /// </summary>
        /// <param name="value">The string value to validate.</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The non-whitespace string value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        /// <exception cref="ArgumentException">Thrown when value is empty or whitespace.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string NotNullOrWhiteSpace(string value, [CallerArgumentExpression("value")] string paramName = null)
        {
            if (value is null)
            {
                throw new ArgumentNullException(paramName);
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"String parameter '{paramName}' cannot be empty or whitespace.", paramName);
            }
            return value;
        }

        /// <summary>
        /// Ensures that the specified collection is not null or empty.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="value">The collection to validate.</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The non-empty collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        /// <exception cref="ArgumentException">Thrown when collection is empty.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IEnumerable<T> NotNullOrEmpty<T>(IEnumerable<T> value, [CallerArgumentExpression("value")] string paramName = null)
        {
            if (value is null)
            {
                throw new ArgumentNullException(paramName);
            }
            
            // Materialize to list to avoid multiple enumeration
            var list = value as IList<T> ?? value.ToList();
            if (!list.Any())
            {
                throw new ArgumentException($"Collection parameter '{paramName}' cannot be empty.", paramName);
            }
            return list;
        }

        /// <summary>
        /// Ensures that the specified value is within the specified range.
        /// </summary>
        /// <typeparam name="T">The type of the value (must be comparable).</typeparam>
        /// <param name="value">The value to validate.</param>
        /// <param name="min">The minimum allowed value (inclusive).</param>
        /// <param name="max">The maximum allowed value (inclusive).</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The value if it's within range.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value is outside the specified range.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T InRange<T>(T value, T min, T max, [CallerArgumentExpression("value")] string paramName = null) 
            where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, 
                    $"Value must be between {min} and {max} (inclusive).");
            }
            return value;
        }

        /// <summary>
        /// Ensures that the specified value is greater than the minimum value.
        /// </summary>
        /// <typeparam name="T">The type of the value (must be comparable).</typeparam>
        /// <param name="value">The value to validate.</param>
        /// <param name="min">The minimum allowed value (exclusive).</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The value if it's greater than minimum.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not greater than minimum.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GreaterThan<T>(T value, T min, [CallerArgumentExpression("value")] string paramName = null) 
            where T : IComparable<T>
        {
            if (value.CompareTo(min) <= 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, 
                    $"Value must be greater than {min}.");
            }
            return value;
        }

        /// <summary>
        /// Ensures that the specified value is greater than or equal to the minimum value.
        /// </summary>
        /// <typeparam name="T">The type of the value (must be comparable).</typeparam>
        /// <param name="value">The value to validate.</param>
        /// <param name="min">The minimum allowed value (inclusive).</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The value if it's greater than or equal to minimum.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value is less than minimum.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GreaterThanOrEqualTo<T>(T value, T min, [CallerArgumentExpression("value")] string paramName = null) 
            where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, 
                    $"Value must be greater than or equal to {min}.");
            }
            return value;
        }

        /// <summary>
        /// Ensures that the specified condition is true.
        /// </summary>
        /// <param name="condition">The condition to validate.</param>
        /// <param name="message">The error message if condition is false.</param>
        /// <exception cref="ArgumentException">Thrown when condition is false.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IsTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message);
            }
        }

        /// <summary>
        /// Ensures that the specified condition is false.
        /// </summary>
        /// <param name="condition">The condition to validate.</param>
        /// <param name="message">The error message if condition is true.</param>
        /// <exception cref="ArgumentException">Thrown when condition is true.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IsFalse(bool condition, string message)
        {
            if (condition)
            {
                throw new ArgumentException(message);
            }
        }

        /// <summary>
        /// Ensures that the specified value satisfies the given predicate.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value to validate.</param>
        /// <param name="predicate">The validation predicate.</param>
        /// <param name="message">The error message if validation fails.</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The validated value.</returns>
        /// <exception cref="ArgumentException">Thrown when predicate returns false.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Satisfies<T>(T value, Func<T, bool> predicate, string message, 
            [CallerArgumentExpression("value")] string paramName = null)
        {
            NotNull(predicate, nameof(predicate));
            
            if (!predicate(value))
            {
                throw new ArgumentException(message, paramName);
            }
            return value;
        }

        /// <summary>
        /// Ensures that the specified file path is valid (not null, empty, or containing invalid characters).
        /// </summary>
        /// <param name="path">The file path to validate.</param>
        /// <param name="paramName">The name of the parameter being validated.</param>
        /// <returns>The validated file path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null.</exception>
        /// <exception cref="ArgumentException">Thrown when path is invalid.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ValidFilePath(string path, [CallerArgumentExpression("path")] string paramName = null)
        {
            NotNullOrWhiteSpace(path, paramName);
            
            var invalidChars = System.IO.Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidChars) >= 0)
            {
                throw new ArgumentException($"Path contains invalid characters: '{path}'", paramName);
            }
            
            return path;
        }
    }
}