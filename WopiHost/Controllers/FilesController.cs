﻿using System.Net;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

using WopiHost.Abstractions;
using WopiHost.Discovery;
using WopiHost.Discovery.Enumerations;
using WopiHost.Models;
using Microsoft.AspNetCore.Authorization;
using WopiHost.Cobalt;

namespace WopiHost.Controllers
{
	/// <summary>
	/// Implementation of WOPI server protocol https://msdn.microsoft.com/en-us/library/hh659001.aspx
	/// </summary>
	[Route("wopi/[controller]")]
	public class FilesController : WopiControllerBase
	{
		private readonly IAuthorizationService _authorizationService;

		private WopiDiscoverer _wopiDiscoverer;


		private WopiDiscoverer WopiDiscoverer
		{
			get { return _wopiDiscoverer ?? (_wopiDiscoverer = new WopiDiscoverer(Configuration.GetSection("WopiClientUrl").Value)); }
		}

		public FilesController(IWopiFileProvider fileProvider, IWopiSecurityHandler securityHandler, IConfiguration configuration, IAuthorizationService authorizationService) : base(fileProvider, securityHandler, configuration)
		{
			_authorizationService = authorizationService;
		}

		private async Task<EditSession> GetEditSessionAsync(string fileId)
		{
			var sessionId = /*Context.Session.GetString("SessionID");
			if (string.IsNullOrEmpty(sessionId))
			{
				sessionId = Guid.NewGuid().ToString();
				Context.Session.SetString("SessionID", sessionId);
			}
			sessionId += "|" +*/ fileId;
			EditSession editSession = SessionManager.Current.GetSession(sessionId);

			if (editSession == null)
			{
				IWopiFile file = FileProvider.GetWopiFile(fileId);

				//TODO: remove hardcoded action 'Edit'
				//TODO: handle all requirements in a generic way (requires="cobalt,containers,update")
				//TODO: http://wopi.readthedocs.io/en/latest/discovery.html#action-requirements
				if (await WopiDiscoverer.RequiresCobaltAsync(file.Extension, WopiActionEnum.Edit))
				{
					editSession = new CobaltSession(file, sessionId);
				}
				else
				{
					editSession = new FileSession(file, sessionId);
				}
				SessionManager.Current.AddSession(editSession);
			}

			return editSession;
		}

		/// <summary>
		/// Returns the metadata about a file specified by an identifier.
		/// Specification: https://msdn.microsoft.com/en-us/library/hh643136.aspx
		/// Example URL: HTTP://server/<...>/wopi*/files/<id>
		/// </summary>
		/// <param name="id">File identifier.</param>
		/// <param name="access_token">Access token used to validate the request.</param>
		/// <returns></returns>
		[HttpGet("{id}")]
		[Produces("application/json")]
		public async Task<CheckFileInfo> GetCheckFileInfo(string id, [FromQuery]string access_token)
		{
			return (await GetEditSessionAsync(id))?.GetCheckFileInfo();
		}

		/// <summary>
		/// Returns contents of a file specified by an identifier.
		/// Specification: https://msdn.microsoft.com/en-us/library/hh657944.aspx
		/// Example URL: HTTP://server/<...>/wopi*/files/<id>/contents
		/// </summary>
		/// <param name="id">File identifier.</param>
		/// <param name="access_token">Access token used to validate the request.</param>
		/// <returns></returns>
		[HttpGet("{id}/contents")]
		[Produces("application/octet-stream")]
		public async Task<ActionResult> GetContents(string id, [FromQuery]string access_token)
		{
			//TODO: implement authorization
			//if (!await _authorizationService.AuthorizeAsync(User, new TokenContainer { FileId = id, Token = access_token }, PolicyNames.HasValidAccessToken))
			//{
			//	return Challenge();
			//}

			var editSession = await GetEditSessionAsync(id);
			//TODO: consider using return new Microsoft.AspNetCore.Mvc.FileStreamResult(editSession.GetFileContent(), "application/octet-stream");
			return new FileContentResult(editSession.GetFileContent(), "application/octet-stream");
		}

		/// <summary>
		/// Updates a file specified by an identifier. (Only for non-cobalt files.)
		/// Specification: https://msdn.microsoft.com/en-us/library/hh657364.aspx
		/// Example URL: HTTP://server/<...>/wopi*/files/<id>/contents
		/// </summary>
		/// <param name="id">File identifier.</param>
		/// <param name="access_token">Access token used to validate the request.</param>
		/// <returns></returns>
		[HttpPut("{id}/contents")]
		[HttpPost("{id}/contents")]
		[Produces("application/octet-stream")]
		public async Task<IActionResult> PutFile(string id, [FromQuery]string access_token)
		{
			var editSession = await GetEditSessionAsync(id);
			editSession.SetFileContent(await HttpContext.Request.Body.ReadBytesAsync());
			return new OkResult();
		}

		/// <summary>
		/// The PutRelativeFile operation creates a new file on the host based on the current file.
		/// </summary>
		/// <param name="id"></param>
		/// <param name="access_token"></param>
		/// <returns></returns>
		public IActionResult PutRelativeFile(string id, [FromQuery] string access_token)
		{
			//TODO: implement according to https://wopirest.readthedocs.io/en/latest/files/PutRelativeFile.html
			return new OkResult();
		}

		/// <summary>
		/// Changes the contents of the file in accordance with [MS-FSSHTTP] and performs other operations like locking.
		/// MS-FSSHTTP Specification: https://msdn.microsoft.com/en-us/library/dd943623.aspx
		/// Specification: https://msdn.microsoft.com/en-us/library/hh659581.aspx
		/// Example URL: HTTP://server/<...>/wopi*/files/<id>
		/// </summary>
		/// <param name="id"></param>
		/// <param name="access_token"></param>
		[HttpPost("{id}")]
		[Produces("application/octet-stream", "text/html")]
		public async Task<IActionResult> PerformAction(string id, [FromQuery]string access_token)
		{
			var editSession = await GetEditSessionAsync(id);
			string wopiOverrideHeader = HttpContext.Request.Headers["X-WOPI-Override"];

			//TODO: Replace the else-ifs with separate methods (https://github.com/petrsvihlik/WopiHost/issues/7)

			if (wopiOverrideHeader.Equals("COBALT"))
			{
				var responseAction = editSession.SetFileContent(await HttpContext.Request.Body.ReadBytesAsync());

				HttpContext.Response.Headers.Add("X-WOPI-CorellationID", HttpContext.Request.Headers["X-WOPI-CorrelationID"]);
				HttpContext.Response.Headers.Add("request-id", HttpContext.Request.Headers["X-WOPI-CorrelationID"]);

				return new Results.FileResult(responseAction, "application/octet-stream");
			}
			else if (wopiOverrideHeader.Equals("LOCK") || wopiOverrideHeader.Equals("UNLOCK") || wopiOverrideHeader.Equals("REFRESH_LOCK"))
			{
				switch (wopiOverrideHeader)
				{
					case "GET_LOCK":
						break;

					case "LOCK":
						break;

					case "UNLOCK":
						break;

					case "REFRESH_LOCK":
						break;
				}
				//TODO: implement locking (https://github.com/petrsvihlik/WopiHost/issues/4)

				return new OkResult();
			}
			else
			{
				// Unsupported action
				return new StatusCodeResult((int)HttpStatusCode.NotImplemented);
			}
		}
	}
}
