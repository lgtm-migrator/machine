﻿namespace Microsoft.AspNetCore.Builder;

public static class IApplicationBuilderExtensions
{
    public static async Task<IApplicationBuilder> UseMachineAsync(this IApplicationBuilder app)
    {
        await app.ApplicationServices.GetService<IRepository<TranslationEngine>>()!.InitAsync();
        await app.ApplicationServices.GetService<IRepository<Build>>()!.InitAsync();
        await app.ApplicationServices.GetService<IRepository<Corpus>>()!.InitAsync();
        await app.ApplicationServices.GetService<IRepository<RWLock>>()!.InitAsync();
        await app.ApplicationServices.GetService<IRepository<TrainSegmentPair>>()!.InitAsync();
        await app.ApplicationServices.GetService<IRepository<Webhook>>()!.InitAsync();
        await app.ApplicationServices.GetService<IRepository<Pretranslation>>()!.InitAsync();

        await app.ApplicationServices.GetService<IDistributedReaderWriterLockFactory>()!.InitAsync();
        app.ApplicationServices.GetService<ITranslationEngineRuntimeService>()?.Init();

        return app;
    }

    public static IApplicationBuilder UseTranslationEngineServer(this IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(e => e.MapGrpcService<GrpcTranslationEngineService>());
        return app;
    }
}
