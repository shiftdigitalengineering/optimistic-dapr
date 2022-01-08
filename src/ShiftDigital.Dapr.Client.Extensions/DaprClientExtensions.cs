using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dapr.Client
{
    public static class DaprClientExtensions
    {
        public static async Task<bool> TrySaveSetStateAsync<TValue>(this DaprClient daprClient, string storeName, string key, Func<IList<TValue>, Task<IList<TValue>>> setFactory, short retryAttempts = 1, short retryWaitTimeInSeconds = 5, CancellationToken cancellationToken = default)
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

        public static async Task<bool> TrySaveSetStateAsync<TValue>(this DaprClient daprClient, string storeName, string key, Func<HashSet<TValue>, Task<HashSet<TValue>>> setFactory, 
                                                                short retryAttempts = 1, short retryWaitTimeInSeconds = 5, CancellationToken cancellationToken = default, ILogger logger = null)
        {
            var (set, etag) = await daprClient.GetStateAndETagAsync<HashSet<TValue>>(storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);

            set = await setFactory(set).ConfigureAwait(false);

            if (logger != null)
            {
                logger.LogInformation("Inside DAPR save set state - key: " + key + ". etag: " + etag +". Begin try sate set state.");
            }
            
            bool result;

            if (set != null)
            {
                result = await daprClient.TrySaveStateAsync(storeName, key, set, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return false;
            }

            // when etag is empty daprClient.TrySaveStateAsync returns "fake" true. below code handles that scenario.
            if (string.IsNullOrWhiteSpace(etag) && result)
            {
                // "This line is GOLD -- DO NOT REMOVE"
                await Task.Delay(new Random().Next(1500, 3500), cancellationToken).ConfigureAwait(false);

                var (onRetrySet, onRetryEtag) = await daprClient.GetStateAndETagAsync<HashSet<TValue>>(storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (logger != null)
                {
                    logger.LogWarning("Inside DAPR save set state - key: " + key + ". etag: " + onRetryEtag + ". On 1st attempt, Empty Etag & Save Result true - begin check if save was success.");
                }

                if (onRetrySet != null && onRetrySet.Count > 0)
                {
                    string itemsToCacheAndCachedItems = "";

                    foreach (TValue t in set)
                    {
                        if (logger != null)
                        {
                            itemsToCacheAndCachedItems += t.ToString() + ", ";
                        }

                        if (!onRetrySet.Contains(t))
                        {
                            if (logger != null)
                            {
                                logger.LogWarning("Inside DAPR save set state - key: " + key + ". etag: " + onRetryEtag + ". On 1st attempt, Empty Etag & Save Result true - item: '" + t.ToString() + "' not exists.");
                            }
                            result = false;
                        }
                    }

                    if (logger != null && !string.IsNullOrWhiteSpace(itemsToCacheAndCachedItems))
                    {
                        itemsToCacheAndCachedItems = itemsToCacheAndCachedItems.Trim();
                        itemsToCacheAndCachedItems = itemsToCacheAndCachedItems.TrimEnd(',');

                        if (onRetrySet != null && onRetrySet.Count > 0)
                        {
                            itemsToCacheAndCachedItems += " | Items in cache: ";
                        }
                    }

                    if (logger != null)
                    {
                        foreach (TValue t in onRetrySet)
                        {
                            itemsToCacheAndCachedItems += t.ToString() + ", ";
                        }

                        if (!string.IsNullOrWhiteSpace(itemsToCacheAndCachedItems))
                        {
                            itemsToCacheAndCachedItems = itemsToCacheAndCachedItems.Trim();
                            itemsToCacheAndCachedItems = itemsToCacheAndCachedItems.TrimEnd(',');
                        }
                    }

                    if (logger != null)
                    {
                        logger.LogInformation("Inside DAPR save set state - key: " + key + ". etag: " + onRetryEtag + ". On 1st attempt, Empty Etag & Save Result true. Items: " + itemsToCacheAndCachedItems);

                        logger.LogInformation("Inside DAPR save set state - key: " + key + ". etag: " + onRetryEtag + ". On 1st attempt, Empty Etag & Save Result true - save was " + (result ? "successful." : "unsuccessful."));
                    }
                }
                else
                {
                    if (logger != null)
                    {
                        logger.LogWarning("Inside DAPR save set state - key: " + key + ". etag: " + onRetryEtag + ". On 1st attempt, Empty Etag & Save Result true - unsuccessful - cached set was empty.");
                    }
                    result = false;
                }
            }

            if (!result)
            {
                if (logger != null)
                {
                    logger.LogWarning("Inside DAPR save set state - key: " + key + ". etag: " + etag + ". On 1st attempt, Save failed - entering retry attempts.");
                }

                while (!result && retryAttempts > 0)
                {
                    if (retryWaitTimeInSeconds > 0)
                    {
                        await Task.Delay(retryWaitTimeInSeconds * 1000, cancellationToken).ConfigureAwait(false);
                    }

                    var (setOnNextAttempt, etagOnNextAttempt) = await daprClient.GetStateAndETagAsync<HashSet<TValue>>(storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (logger != null)
                    {
                        logger.LogInformation("Inside DAPR save set state - key: " + key + ". etag: " + etagOnNextAttempt + ". Retry attempt: " + retryAttempts);
                    }

                    setOnNextAttempt = await setFactory(setOnNextAttempt).ConfigureAwait(false);

                    result = await daprClient.TrySaveStateAsync(storeName, key, setOnNextAttempt, etagOnNextAttempt, cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (logger != null)
                    {
                        string msg = "Inside DAPR save set state - key: " + key + ". etag: " + etagOnNextAttempt + ". Retry attempt: " + retryAttempts + ". Try save set state result: " + result.ToString();
                        if (result)
                        {
                            logger.LogInformation(msg);
                        }
                        else
                        {
                            logger.LogWarning(msg);
                        }
                    }

                    retryAttempts = (short)(retryAttempts - 1);
                }

                if (!result)
                    return result;
            }
            else
            {
                if (logger != null)
                {
                    logger.LogInformation("Inside DAPR save set state - key: " + key + ". etag: " + etag + ". Save success on first attempt.");
                }
            }
            return result;
        }
    }
}
