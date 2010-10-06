﻿using System;

namespace Deveel.Data.Net.Client {
	public sealed class MessageError {
		private readonly string message;
		private string stackTrace;
		private readonly string source;

		public MessageError(string source, string message, string stackTrace) {
			this.source = source;
			this.stackTrace = stackTrace;
			this.message = message;
		}

		public MessageError(string source, string message)
			: this(source, message, null) {
		}

		public MessageError(string message)
			: this(null, message) {
		}

		public MessageError(Exception exception)
			: this(exception.GetType().Namespace + "." + exception.GetType().Namespace, exception.Message, exception.StackTrace) {
		}

		public string StackTrace {
			get { return stackTrace; }
			set { stackTrace = value; }
		}

		public string Source {
			get { return source; }
		}

		public string Message {
			get { return message; }
		}
	}
}