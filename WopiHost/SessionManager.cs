﻿using System;
using System.Collections.Generic;
using System.Threading;

namespace WopiHost
{
	public class SessionManager
	{
		//TODO: consider using ConcurrentDictionary
		private static volatile SessionManager _current;
		private static readonly object _syncObj = new object();
		private readonly Dictionary<string, EditSession> _sessions = new Dictionary<string, EditSession>();
		private readonly int m_timeout = 60 * 60 * 1000;
		private readonly int m_closewait = 3 * 60 * 60;
		private readonly Timer timer;

		public static SessionManager Current
		{
			get
			{
				if (_current == null)
				{
					lock (_syncObj)
					{
						if (_current == null)
						{
							_current = new SessionManager();
						}
					}
				}
				return _current;
			}
		}

		public SessionManager()
		{
			timer = new Timer(CleanUp, null, m_timeout, Timeout.Infinite);
		}

		public EditSession GetSession(string sessionId)
		{
			EditSession es;

			lock (_syncObj)
			{
				if (!_sessions.TryGetValue(sessionId, out es))
				{
					return null;
				}
			}

			return es;
		}

		public void AddSession(EditSession session)
		{
			lock (_syncObj)
			{
				_sessions.Add(session.SessionId, session);
			}
		}

		public void DelSession(EditSession session)
		{
			lock (_syncObj)
			{
				// Clean up
				session.Dispose();
				_sessions.Remove(session.SessionId);
			}
		}

		private void CleanUp(object stateInfo)
		{
			lock (_syncObj)
			{
				foreach (var session in _sessions.Values)
				{
					if (session.LastUpdated.AddSeconds(m_closewait) < DateTime.Now)
					{
						// Clean up
						session.Dispose();
						_sessions.Remove(session.SessionId);
					}
				}
				timer.Change(m_timeout, Timeout.Infinite);
			}
		}
	}
}
