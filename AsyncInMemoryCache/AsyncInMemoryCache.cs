using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace AsyncInMemoryCache
{
    public class AsyncInMemoryCache
    {
        protected IMemoryCache mMemoryCache;
        protected Dictionary<string, RegisteredCache> mRegisteredCaches = new Dictionary<string, RegisteredCache>();
        protected ILogger<AsyncInMemoryCache> mLogger;
        private const int RETRY_LOADING_ATTEMPTS = 3;

        public AsyncInMemoryCache(IMemoryCache aMemoryCache, ILogger<AsyncInMemoryCache> aLogger)
        {
            mMemoryCache = aMemoryCache;
            mLogger = aLogger;
        }

        public virtual void ResetCache(string aKey)
        {
            if (mRegisteredCaches.ContainsKey(aKey))
            {
                mRegisteredCaches.Remove(aKey);
            }
        }

        public virtual object GetData(string aKey)
        {
            if (mRegisteredCaches.ContainsKey(aKey))
            {
                RegisteredCache lCache = mRegisteredCaches[aKey];

                int lCounter = 0;

                while (!lCache.IsDataLoaded && lCounter < 100)
                {
                    lCounter++;
                    Thread.Sleep(500);
                }

                lock (lCache.ReadLock)
                {
                    if (mMemoryCache.TryGetValue(lCache.Key, out object lData))
                    {
                        return lData;
                    }
                    return null;
                }
            }
            else throw new ApplicationException($"Cache '{aKey}' doesn't exists.");
        }
        public T GetData<T>(string aKey)
        {
            return (T)GetData(aKey);
        }
        public void RegisterCache(string aKey, int aTimeout_ms, LoadDataDelegate aLoadDataDelegate)
        {
            if (mRegisteredCaches.ContainsKey(aKey)) return;

            var lCache = new RegisteredCache()
            {
                Key = aKey,
                Workload = aLoadDataDelegate
            };
            Timer lTimer = new Timer(TimerTickAsync, lCache, 1000, aTimeout_ms);
            lCache.Timer = lTimer;
            mRegisteredCaches.Add(aKey, lCache);
        }

        private void TimerTickAsync(object aState)
        {
            RegisteredCache lCache = (RegisteredCache)aState;

            lock (lCache.LoadLock)
            {
                if (lCache.LoadingData) return;
                lCache.LoadingData = true;
            }

            for (int i = 0; i < RETRY_LOADING_ATTEMPTS; i++)
            {
                try
                {
                    object lData = lCache.Workload();
                    lock (lCache.ReadLock)
                    {
                        mMemoryCache.Set(lCache.Key, lData);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    mLogger.LogError(ex, "Cache load error.");
                }
            }
            lCache.LoadingData = false;
            lCache.IsDataLoaded = true;
        }

        public delegate object LoadDataDelegate();
        protected class RegisteredCache
        {
            public string Key { get; set; }
            public Timer Timer { get; set; }
            public LoadDataDelegate Workload { get; set; }
            public bool LoadingData { get; set; }
            public object LoadLock { get; } = new object();
            public object ReadLock { get; } = new object();
            public bool IsDataLoaded { get; set; }
        }
    }
}
