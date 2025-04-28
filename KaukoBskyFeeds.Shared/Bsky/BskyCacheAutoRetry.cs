using Castle.DynamicProxy;
using FishyFlip;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Shared.Bsky;

public static class BskyCacheAutoRetry
{
    public static IBskyCache Create(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<BskyCacheAutoRetryGenerator>>();
        var proto = serviceProvider.GetRequiredService<ATProtocol>();

        var originalClass = ActivatorUtilities.CreateInstance<BskyCache>(serviceProvider);
        var generator = new ProxyGenerator();
        var interceptor = new BskyCacheAutoRetryGenerator(logger, proto);
        var proxy = generator.CreateInterfaceProxyWithTargetInterface<IBskyCache>(
            originalClass,
            interceptor
        );
        return proxy;
    }

    private class BskyCacheAutoRetryGenerator(
        ILogger<BskyCacheAutoRetryGenerator> logger,
        ATProtocol proto
    ) : IAsyncInterceptor
    {
        public void InterceptSynchronous(IInvocation invocation)
        {
            // not concerned with this atm
            invocation.Proceed();
        }

        public void InterceptAsynchronous(IInvocation invocation)
        {
            // not concerned with this atm
            invocation.Proceed();
        }

        public void InterceptAsynchronous<TResult>(IInvocation invocation)
        {
            invocation.ReturnValue = this.InternalInterceptAsynchronous<TResult>(invocation);
        }

        private async Task<TResult> InternalInterceptAsynchronous<TResult>(IInvocation invocation)
        {
            invocation.Proceed();
            var task = (Task<TResult>)invocation.ReturnValue;

            try
            {
                return await task;
            }
            catch (ATNetworkErrorException ex)
            {
                if (ex.AtError.StatusCode != 400)
                {
                    logger.LogWarning(
                        ex,
                        "Got an ATNetworkErrorException during BskyCache.{name}",
                        invocation.MethodInvocationTarget.Name
                    );
                    throw;
                }

                // Retry once
                logger.LogWarning(
                    ex,
                    "Got an ATNetworkErrorException during BskyCache.{name}, retrying once",
                    invocation.MethodInvocationTarget.Name
                );

                await proto.SessionManager.RefreshSessionAsync(); // refresh the session first
                invocation.Proceed();
                var taskRetry = (Task<TResult>)invocation.ReturnValue;
                return await taskRetry;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Got an unhandled error during BskyCache.{name}",
                    invocation.MethodInvocationTarget.Name
                );
                throw;
            }
        }
    }
}
