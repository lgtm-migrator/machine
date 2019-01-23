﻿using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SIL.Machine.WebApi.DataAccess;
using SIL.Machine.WebApi.Models;
using SIL.Machine.WebApi.Services;
using SIL.Machine.WebApi.Utils;

namespace SIL.Machine.WebApi
{
	public static class IApplicationBuilderExtensions
	{
		public static IApplicationBuilder UseMachine(this IApplicationBuilder app)
		{
			app.ApplicationServices.GetService<IEngineRepository>().InitAsync().WaitAndUnwrapException();
			app.ApplicationServices.GetService<IBuildRepository>().InitAsync().WaitAndUnwrapException();
			app.ApplicationServices.GetService<IRepository<Project>>().InitAsync().WaitAndUnwrapException();

			app.ApplicationServices.GetService<EngineService>().Init();

			return app;
		}
	}
}
