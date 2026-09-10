using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SephPlanner.Core.Combat
{
    internal static class CombatFingerprint
    {
        internal static string Of(object? value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        private static void Write(StringBuilder builder, object? value)
        {
            if (value == null) { builder.Append("null;"); return; }
            if (value is string text) { builder.Append(text.Length).Append(':').Append(text); return; }
            if (value is IDictionary dictionary)
            {
                builder.Append('{');
                foreach (var key in dictionary.Keys.Cast<string>().OrderBy(key => key, StringComparer.Ordinal))
                { Write(builder, key); Write(builder, dictionary[key]); }
                builder.Append('}');
            }
            else if (value is IEnumerable sequence)
            {
                builder.Append('[');
                foreach (var item in sequence) Write(builder, item);
                builder.Append(']');
            }
            else if (value.GetType().IsPrimitive || value.GetType().IsEnum)
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';');
            else
            {
                builder.Append('(');
                foreach (var property in value.GetType().GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal))
                { Write(builder, property.Name); Write(builder, property.GetValue(value)); }
                builder.Append(')');
            }
        }
    }
}
