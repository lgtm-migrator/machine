﻿using System;

namespace SIL.Machine.WebApi.Server.DataAccess
{
	public class ConcurrencyConflictException : Exception
	{
		public ConcurrencyConflictException(string message)
			: base(message)
		{
		}

		public ConcurrencyConflictException(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
