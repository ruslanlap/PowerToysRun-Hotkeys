// ===== 9. ServiceContainer.cs (Optional Dependency Injection Container) =====
using System;
using System.Collections.Generic;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public interface IServiceContainer
    {
        void RegisterSingleton<TInterface, TImplementation>() 
            where TImplementation : class, TInterface;
        void RegisterSingleton<TInterface>(TInterface instance);
        void RegisterTransient<TInterface, TImplementation>() 
            where TImplementation : class, TInterface;
        TInterface Resolve<TInterface>();
        void Dispose();
    }

    public class ServiceContainer : IServiceContainer, IDisposable
    {
        private readonly Dictionary<Type, object> _singletons = new();
        private readonly Dictionary<Type, Type> _transients = new();
        private readonly List<IDisposable> _disposables = new();
        private bool _disposed;

        public void RegisterSingleton<TInterface, TImplementation>() 
            where TImplementation : class, TInterface
        {
            _singletons[typeof(TInterface)] = typeof(TImplementation);
        }

        public void RegisterSingleton<TInterface>(TInterface instance)
        {
            _singletons[typeof(TInterface)] = instance;
            if (instance is IDisposable disposable)
                _disposables.Add(disposable);
        }

        public void RegisterTransient<TInterface, TImplementation>() 
            where TImplementation : class, TInterface
        {
            _transients[typeof(TInterface)] = typeof(TImplementation);
        }

        public TInterface Resolve<TInterface>()
        {
            var interfaceType = typeof(TInterface);

            if (_singletons.TryGetValue(interfaceType, out var singleton))
            {
                if (singleton is Type singletonType)
                {
                    var instance = (TInterface)Activator.CreateInstance(singletonType);
                    _singletons[interfaceType] = instance;
                    if (instance is IDisposable disposable)
                        _disposables.Add(disposable);
                    return instance;
                }
                return (TInterface)singleton;
            }

            if (_transients.TryGetValue(interfaceType, out var transientType))
            {
                var instance = (TInterface)Activator.CreateInstance(transientType);
                if (instance is IDisposable disposable)
                    _disposables.Add(disposable);
                return instance;
            }

            throw new InvalidOperationException($"Service of type {interfaceType.Name} is not registered.");
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var disposable in _disposables)
            {
                try
                {
                    disposable?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }

            _disposables.Clear();
            _singletons.Clear();
            _transients.Clear();
            _disposed = true;
        }
    }
}