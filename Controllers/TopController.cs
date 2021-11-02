using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PlayerService.Models;
using Rumble.Platform.Common.Web;
using PlayerService.Services;

namespace PlayerService.Controllers
{
	[ApiController, Route("player")]
	public class TopController : PlatformController
	{
		private readonly InstallationService _installService;
		
		public TopController(InstallationService installService, IConfiguration config) : base(config)
		{
			_installService = installService;
		}

		[HttpGet, Route("health")]
		public override ActionResult HealthCheck()
		{
			return Ok(_installService.HealthCheckResponseObject);
		}
	}
}