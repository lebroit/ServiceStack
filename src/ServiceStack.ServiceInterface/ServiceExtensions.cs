﻿using System;
using System.Net;
using ServiceStack.CacheAccess;
using ServiceStack.CacheAccess.Providers;
using ServiceStack.Common.Web;
using ServiceStack.Redis;
using ServiceStack.ServiceHost;
using ServiceStack.ServiceInterface.Auth;
using ServiceStack.Text;
using ServiceStack.WebHost.Endpoints;

namespace ServiceStack.ServiceInterface
{
	public static class ServiceExtensions
	{
		public static string AddQueryParam(this string url, string key, object val)
		{
			return url.AddQueryParam(key, val.ToString());
		}

		public static string AddQueryParam(this string url, object key, string val)
		{
			return AddQueryParam(url, (key ?? "").ToString(), val);
		}

		public static string AddQueryParam(this string url, string key, string val)
		{
			if (string.IsNullOrEmpty(url)) return null;
			var prefix = url.IndexOf('?') == -1 ? "?" : "&";
			return url + prefix + key + "=" + val.UrlEncode();
		}

		public static string AddHashParam(this string url, string key, object val)
		{
			return url.AddHashParam(key, val.ToString());
		}

		public static string AddHashParam(this string url, string key, string val)
		{
			if (string.IsNullOrEmpty(url)) return null;
			var prefix = url.IndexOf('#') == -1 ? "#" : "/";
			return url + prefix + key + "=" + val.UrlEncode();
		}

		public static IHttpResult Redirect(this IServiceBase service, string url)
		{
			return service.Redirect(url, "Moved Temporarily");
		}

		public static IHttpResult Redirect(this IServiceBase service, string url, string message)
		{
			return new HttpResult(HttpStatusCode.Redirect, message) {
				ContentType = service.RequestContext.ResponseContentType,
				Headers = {
					{ HttpHeaders.Location, url }
				},
			};
		}

		public static IHttpResult AuthenticationRequired(this IServiceBase service)
		{
			return new HttpResult {
				StatusCode = HttpStatusCode.Unauthorized,
				ContentType = service.RequestContext.ResponseContentType,
				Headers = {
					{ HttpHeaders.WwwAuthenticate, AuthService.DefaultOAuthProvider + " realm=\"{0}\"".Fmt(AuthService.DefaultOAuthRealm) }
				},
			};
		}

		public static string GetSessionId(this IServiceBase service)
		{
			var req = service.RequestContext.Get<IHttpRequest>();
			var id = req.GetSessionId();
			if (id == null)
				throw new ArgumentNullException("Session not set. Is Session being set in RequestFilters?");

			return id;
		}

		/// <summary>
		/// If they don't have an ICacheClient configured use an In Memory one.
		/// </summary>
		private static readonly MemoryCacheClient DefaultCache = new MemoryCacheClient { FlushOnDispose = true };

		public static ICacheClient GetCacheClient(this IResolver service)
		{
			return service.TryResolve<ICacheClient>()
				?? (ICacheClient)service.TryResolve<IRedisClientsManager>()
				?? DefaultCache;
		}

		public static void SaveSession(this IServiceBase service, IAuthSession session, TimeSpan? expiresIn=null)
		{
			if (service == null) return;

			service.RequestContext.Get<IHttpRequest>().SaveSession(session, expiresIn);
		}

		public static void RemoveSession(this IServiceBase service)
		{
			if (service == null) return;

			service.RequestContext.Get<IHttpRequest>().RemoveSession();
		}

		public static void CacheSet<T>(this ICacheClient cache, string key, T value, TimeSpan? expiresIn)
		{
			if (expiresIn.HasValue)
				cache.Set(key, value, expiresIn.Value);
			else
				cache.Set(key, value);
		}

		public static void SaveSession(this IHttpRequest httpReq, IAuthSession session, TimeSpan? expiresIn=null)
		{
			if (httpReq == null) return;

			using (var cache = httpReq.GetCacheClient())
			{
				var sessionKey = SessionFeature.GetSessionKey(httpReq.GetSessionId());
				cache.CacheSet(sessionKey, session, expiresIn ?? AuthFeature.GetDefaultSessionExpiry());
			}

			httpReq.Items[RequestItemsSessionKey] = session;
		}

		public static void RemoveSession(this IHttpRequest httpReq)
		{
			if (httpReq == null) return;

			using (var cache = httpReq.GetCacheClient())
			{
				var sessionKey = SessionFeature.GetSessionKey(httpReq.GetSessionId());
				cache.Remove(sessionKey);
			}

			httpReq.Items.Remove(RequestItemsSessionKey);
		}

		public static IAuthSession GetSession(this IServiceBase service, bool reload = false)
		{
			return service.RequestContext.Get<IHttpRequest>().GetSession(reload);
		}

		public const string RequestItemsSessionKey = "__session";
		public static IAuthSession GetSession(this IHttpRequest httpReq, bool reload = false)
		{
			if (httpReq == null) return null;

			object oSession = null;
			if (!reload)
				httpReq.Items.TryGetValue(RequestItemsSessionKey, out oSession);

			if (oSession != null)
				return (IAuthSession)oSession;

			using (var cache = httpReq.GetCacheClient())
			{
				var session = GetSession(cache, httpReq.GetSessionId());
				if (session != null)
					httpReq.Items.Add(RequestItemsSessionKey, session);
				return session;
			}
		}

		public static IAuthSession GetSession(this ICacheClient cache, string sessionId)
		{
			var session = cache.Get<IAuthSession>(SessionFeature.GetSessionKey(sessionId));
			if (session == null)
			{
				session = AuthService.CurrentSessionFactory();
				session.Id = sessionId;
				session.CreatedAt = session.LastModified = DateTime.UtcNow;
			}
			return session;
		}

	}
}