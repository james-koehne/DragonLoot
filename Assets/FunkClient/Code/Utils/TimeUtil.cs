using System;

public static class TimeUtil
{
	public static bool IsWithinTimeSeconds( string unixTime, int seconds )
	{
		DateTime createTimeDateTime = DateTime.Parse( unixTime, null, System.Globalization.DateTimeStyles.RoundtripKind );

		DateTime currentTime = DateTime.UtcNow;

		TimeSpan timeDifference = currentTime - createTimeDateTime;

		return timeDifference.TotalSeconds <= seconds;
	}
	public static bool IsWithinTimeSeconds( long unixTime, int seconds )
	{
		DateTime createTimeDateTime = DateTimeOffset.FromUnixTimeSeconds( unixTime ).UtcDateTime;

		DateTime currentTime = DateTime.UtcNow;

		TimeSpan timeDifference = currentTime - createTimeDateTime;

		return timeDifference.TotalSeconds <= seconds;
	}

	public static bool TryGetAge( string timestamp, out TimeSpan age )
	{
		age = TimeSpan.Zero;

		if ( string.IsNullOrWhiteSpace( timestamp ) )
			return false;

		DateTime createdTimeUtc;

		// Try parsing as UNIX timestamp
		if ( long.TryParse( timestamp, out long unixSeconds ) )
		{
			try
			{
				createdTimeUtc = DateTimeOffset.FromUnixTimeSeconds( unixSeconds ).UtcDateTime;
			}
			catch
			{
				return false;
			}
		}
		else
		{
			// Try parsing as ISO 8601
			if ( !DateTime.TryParse(
				timestamp,
				null,
				System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
				out createdTimeUtc ) )
			{
				return false;
			}
		}

		age = DateTime.UtcNow - createdTimeUtc;
		return true;
	}

	public static string GetFormattedTime( float runTime )
	{
		TimeSpan timeSpan = TimeSpan.FromSeconds( runTime );
		string formattedTime = timeSpan.ToString( @"hh\:mm\:ss\.fff" );

		return formattedTime;
	}

	public static string GetFormattedDaysHoursMinutes( float playTime )
	{
		TimeSpan timeSpan = TimeSpan.FromSeconds( playTime );
		string playTimeFormatted = String.Format( "{0}d {1}h {2}m", timeSpan.Days, timeSpan.Hours, timeSpan.Minutes );

		return playTimeFormatted;
	}
}
