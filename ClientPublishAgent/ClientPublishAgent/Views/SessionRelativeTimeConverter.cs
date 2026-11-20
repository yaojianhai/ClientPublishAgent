using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClientPublishAgent.Views;

public sealed class SessionRelativeTimeConverter : IMultiValueConverter
{
	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
		values.Count >= 2 && values[0] is DateTimeOffset updated && values[1] is DateTimeOffset now
			? Format(updated, now) : "";

	public static string Format(DateTimeOffset updated, DateTimeOffset now)
	{
		var elapsed = now - updated;
		if (elapsed.TotalMinutes < 1) return "刚刚";
		if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}分钟前";
		if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}小时前";
		if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays}天前";
		if (elapsed.TotalDays < 365) return $"{(int)(elapsed.TotalDays / 30)}个月前";
		return $"{(int)(elapsed.TotalDays / 365)}年前";
	}
}