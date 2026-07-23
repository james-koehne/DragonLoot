using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

using UnityEngine;
using System.Net.Http;

public static class FunkCloudUtils
{
	public struct Response
	{
		public bool success;
		public string result;
	}

	/// <summary>
	/// Extension method for <see cref="string"/> using <see cref="string.IsNullOrWhiteSpace(string)"/>.
	/// </summary>
	public static bool IsEmpty( this string str )
		=> string.IsNullOrWhiteSpace( str );

	/// <summary>
	/// Extension method for all collection types to see if it is null or empty.
	/// </summary>
	public static bool IsNullOrEmpty<T>( this IEnumerable<T> enumerable )
	{
		return enumerable switch
		{
			null => true,
			ICollection collection => collection.Count == 0,
			_ => !enumerable.Any()
		};
	}

	public static bool IsValidUsername( string input )
	{
		if ( string.IsNullOrEmpty( input ) ) return false; // Handle null or empty strings
		return Regex.IsMatch( input, @"^[a-zA-Z0-9]+$" );
	}

	public static bool IsValidPassword( string password )
	{
		string pattern = @"^[a-zA-Z0-9!@#$%^&*()_+={}\[\]:;,.<>?`\`-]*$";
		return Regex.IsMatch( password, pattern );
	}

	public static bool IsValidEmailRegex( string email )
	{
		string emailRegex = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
		return Regex.IsMatch( email, emailRegex );
	}

	public static Response IsValidEmail( string email )
	{
		if ( !IsValidEmailRegex( email ) )
		{
			Debug.Log( "IsValidEmail: Regex doesn't fit" );
			return new Response() { success = false, result = "Invalid email format" };
		}

		return new Response() { success = true, result = "" };
	}

	public static async Task<Response> IsValidEmailStrict( string email )
	{
		if ( !IsValidEmailRegex( email ) )
		{
			Debug.Log( "IsValidEmail: Regex doesn't fit" );
			return new Response() { success = false, result = "Invalid email format" };
		}

		string domain = email.Split( '@' )[ 1 ];

		if ( !HasValidDomain( domain ) )
		{
			Debug.Log( "IsValidEmail: Domain is not valid (" + domain + ")" );
			return new Response() { success = false, result = "Email domain invalid" };
		}

		bool mxRecordExist = await QueryMXRecordsAsync( domain );

		if ( !mxRecordExist )
		{
			Debug.Log( "IsValidEmail: MX Record/s do not exist (" + domain + ")" );
			return new Response() { success = false, result = "Email domain doesn't exist" };
		}

		return new Response() { success = true, result = "" };
	}

	public static bool HasValidDomain( string domain )
	{
		try
		{
			IPHostEntry host = Dns.GetHostEntry( domain ); // Resolve domain
			return host != null;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<bool> QueryMXRecordsAsync( string domain )
	{
		try
		{
			// Build the request URL for MX records query
			string url = $"https://dns.google/resolve?name={domain}&type=MX";

			using ( HttpClient client = new HttpClient() )
			{
				string response = await client.GetStringAsync( url );
				return ParseMXRecords( response );
			}
		}
		catch ( Exception ex )
		{
			Debug.LogError( "Error querying MX records: " + ex.Message );
			return false;
		}
	}

	// Parse the MX records and return true if any are found
	private static bool ParseMXRecords( string response )
	{
		// Deserialize the response JSON into an object
		DnsResponse dnsResponse = JsonUtility.FromJson<DnsResponse>( response );

		// Check if there are any MX records in the answer
		return dnsResponse.Answer != null && dnsResponse.Answer.Length > 0;
	}

	public static string GetISONowTime()
	{
		return DateTimeOffset.UtcNow.ToString( "o" );
	}

	public static string GetISOEpochTime()
	{
		return DateTimeOffset.UnixEpoch.ToString( "o" );
	}

	// Class to represent the structure of the DNS API response
	[Serializable]
	public class DnsResponse
	{
		public string Status;
		public Answer[] Answer;
	}

	// Class to represent each Answer object (the MX records)
	[Serializable]
	public class Answer
	{
		public string Name;
		public int Type;
		public int Priority;
		public string Data;
	}
}