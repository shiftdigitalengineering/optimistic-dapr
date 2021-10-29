using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dapr.Client
{
    public static class DaprClientExtensions
    {
        public static async Task<bool> TrySaveSetStateAsync<TValue>(this DaprClient daprClient, string storeName, string key, Func<IList<TValue>, Task<IList<TValue>>> setFactory,  short retryAttempts = 1, short retryWaitTimeInSeconds = 5, CancellationToken cancellationToken = default)            
        {
            var (set, etag) = await daprClient.GetStateAndETagAsync<IList<TValue>>(storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
            
            set = await setFactory(set).ConfigureAwait(false);

            var result = await daprClient.TrySaveStateAsync(storeName, key, set, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result)
            {
                while (!result && retryAttempts > 0)
                {
                    if (retryWaitTimeInSeconds > 0)
                    {
                        await Task.Delay(retryWaitTimeInSeconds * 1000, cancellationToken).ConfigureAwait(false);
                    }

                    retryAttempts = (short)(retryAttempts - 1);

                    var (setOnNextAttempt, etagOnNextAttempt) = await daprClient.GetStateAndETagAsync<IList<TValue>>(storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);                    

                    setOnNextAttempt = await setFactory(setOnNextAttempt).ConfigureAwait(false);

                    result = await daprClient.TrySaveStateAsync(storeName, key, setOnNextAttempt, etagOnNextAttempt, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (!result)
                    return result;
            }
            return result;
        }
    }
}
