using System;
using System.Collections.Generic;

namespace HKFeedbackV2
{
    internal sealed class Container : IContainer
    {
        private readonly Dictionary<Type, object> anonymousObjects = new();

        private readonly Dictionary<ContainerKey, object> keyedObjects = new();

        IContainerRegister IContainerRegister.Register<T>(T obj)
        {
            anonymousObjects[typeof(T)] = obj;
            return this;
        }

        IContainerRegister IContainerRegister.Register<T>(ContainerKey key, T obj)
        {
            keyedObjects[key] = obj;
            return this;
        }

        T IContainerResolve.Resolve<T>()
        {
            if (anonymousObjects.TryGetValue(typeof(T), out var obj))
            {
                return (T)obj;
            }

            throw new InvalidOperationException($"Type {typeof(T)} is not registered.");
        }

        T IContainerResolve.Resolve<T>(ContainerKey key)
        {
            if (keyedObjects.TryGetValue(key, out var obj))
            {
                return (T)obj;
            }

            throw new InvalidOperationException($"Key {key.name} is not registered.");
        }

        bool IContainerResolve.TryResolve<T>(out T value)
        {
            if (anonymousObjects.TryGetValue(typeof(T), out var obj))
            {
                value = (T)obj;
                return true;
            }

            value = default;
            return false;
        }

        bool IContainerResolve.TryResolve<T>(ContainerKey key, out T value)
        {
            if (keyedObjects.TryGetValue(key, out var obj))
            {
                value = (T)obj;
                return true;
            }

            value = default;
            return false;
        }
    }
}
