﻿using Fluid.Accessors;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Fluid
{
  public static class MemberAccessStrategyExtensions
    {
        internal static readonly ConcurrentDictionary<string, IMemberAccessor> NamedAccessors = new ConcurrentDictionary<string, IMemberAccessor>();
        private static readonly ConcurrentDictionary<Type, List<string>> TypeMembers = new ConcurrentDictionary<Type, List<string>>();
        private static readonly Func<Type, string, string> KeyGenerator = (type, name) => string.Concat("(", type.FullName, ")", name);

        private static List<string> GetAllMembers(Type type) => TypeMembers.GetOrAdd(type, t =>
        {
            var result = new List<string>();

            foreach (var propertyInfo in type.GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                result.Add(propertyInfo.Name);
                NamedAccessors.TryAdd(KeyGenerator(type, propertyInfo.Name), new MethodInfoAccessor(propertyInfo.GetGetMethod()));
            }

            foreach (var fieldInfo in type.GetTypeInfo().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                result.Add(fieldInfo.Name);
                NamedAccessors.TryAdd(KeyGenerator(type, fieldInfo.Name), new DelegateAccessor((o, n) => fieldInfo.GetValue(o)));
            }

            return result;
        });

        public static IMemberAccessor GetNamedAccessor(Type type, string name) => NamedAccessors.GetOrAdd(KeyGenerator(type, name), k =>
        {
            var propertyInfo = type.GetTypeInfo().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

            if (propertyInfo != null)
                return new MethodInfoAccessor(propertyInfo.GetGetMethod());

            var fieldInfo = type.GetTypeInfo().GetField(name, BindingFlags.Public | BindingFlags.Instance);

            if (fieldInfo != null)
                return new DelegateAccessor((o, n) => fieldInfo.GetValue(o));

            throw new InvalidOperationException("MemberAccessor not found.");
        });


        /// <summary>
        /// Registers a type and all its public properties.
        /// </summary>
        /// <typeparam name="T">The type to register.</typeparam>
        public static void Register<T>(this IMemberAccessStrategy strategy)
        {
            strategy.Register(typeof(T));
        }

        /// <summary>
        /// Registers a type and all its public properties.
        /// </summary>
        /// <param name="type">The type to register.</param>
        public static void Register(this IMemberAccessStrategy strategy, Type type)
        {
            foreach (var name in GetAllMembers(type))
            {
                strategy.Register(type, name, GetNamedAccessor(type, name));
            }
        }

        /// <summary>
        /// Registers a limited set of properties in a type.
        /// </summary>
        /// <typeparam name="T">The type to register.</typeparam>
        /// <param name="names">The names of the properties in the type to register.</param>
        public static void Register<T>(this IMemberAccessStrategy strategy, params string[] names)
        {
            strategy.Register(typeof(T), names);
        }

        /// <summary>
        /// Registers a limited set of properties in a type.
        /// </summary>
        /// <typeparam name="T">The type to register.</typeparam>
        /// <param name="names">The property's expressions in the type to register.</param>
        public static void Register<T>(this IMemberAccessStrategy strategy, params Expression<Func<T, object>>[] names)
        {
            strategy.Register<T>(names.Select(ExpressionHelper.GetPropertyName).ToArray());
        }

        /// <summary>
        /// Registers a limited set of properties in a type.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="names">The names of the properties in the type to register.</param>
        public static void Register(this IMemberAccessStrategy strategy, Type type, params string[] names)
        {
            foreach (var name in names)
            {
                strategy.Register(type, name, GetNamedAccessor(type, name));
            }
        }

        /// <summary>
        /// Registers a named property when accessing a type using a <see cref="IMemberAccessor"/>
        /// to retrieve the value. The name of the property doesn't have to exist on the object.
        /// </summary>
        /// <typeparam name="T">The type to register.</typeparam>
        /// <param name="name">The name of the property to intercept.</param>
        /// <param name="getter">The <see cref="IMemberAccessor"/> instance used to retrieve the value.</param>
        public static void Register<T>(this IMemberAccessStrategy strategy, string name, IMemberAccessor getter)
        {
            strategy.Register(typeof(T), name, getter);
        }

        /// <summary>
        /// Registers a type using a <see cref="IMemberAccessor"/> to retrieve any of
        /// its property values.
        /// </summary>
        /// <typeparam name="T">The type to register.</typeparam>
        /// <param name="getter">The <see cref="IMemberAccessor"/> instance used to retrieve the value.</param>
        public static void Register<T>(this IMemberAccessStrategy strategy, IMemberAccessor getter)
        {
            strategy.Register(typeof(T), "*", getter);
        }

        /// <summary>
        /// Registers a type using a <see cref="IMemberAccessor"/> to retrieve any of
        /// its property values.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="getter">The <see cref="IMemberAccessor"/> instance used to retrieve the value.</param>
        public static void Register(this IMemberAccessStrategy strategy, Type type, IMemberAccessor getter)
        {
            strategy.Register(type, "*", getter);
        }

        /// <summary>
        /// Registers a type with a <see cref="Func{T, string, TResult}"/> to retrieve any of
        /// its property values.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="accessor">The <see cref="Func{T, string, TResult}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, Func<T, string, TResult> accessor)
        {
            Register<T, TResult>(strategy, (obj, name, ctx) => accessor(obj, name));
        }

        /// <summary>
        /// Registers a type with a <see cref="Func{T, string, TemplateContext, TResult}"/> to retrieve any of
        /// its property values.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="accessor">The <see cref="Func{T, string, TemplateContext, TResult}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, Func<T, string, TemplateContext, TResult> accessor)
        {
            strategy.Register(typeof(T), "*", new DelegateAccessor<T, TResult>(accessor));
        }

        /// <summary>
        /// Registers a type with a <see cref="Func{T, string, Task{TResult}}"/> to retrieve any of
        /// its property values.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="accessor">The <see cref="Func{T, string, Task{Object}}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, Func<T, string, Task<TResult>> accessor)
        {
            Register<T, TResult>(strategy, (obj, name, ctx) => accessor(obj, name));
        }

        /// <summary>
        /// Registers a type with a <see cref="Func{T, string, TemplateContext, Task{TResult}}"/> to retrieve any of
        /// its property values.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="accessor">The <see cref="Func{T, string, TemplateContext, Task{TResult}}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, Func<T, string, TemplateContext, Task<TResult>> accessor)
        {
            strategy.Register(typeof(T), "*", new AsyncDelegateAccessor<T, TResult>(accessor));
        }

        /// <summary>
        /// Registers a type with a <see cref="Func{T, Task{TResult}}"/> to retrieve the given property's value.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="name">The name of the property.</param>
        /// <param name="accessor">The <see cref="Func{T, Task{TResult}}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, string name, Func<T, Task<TResult>> accessor)
        {
            Register<T, TResult>(strategy, name, (obj, ctx) => accessor(obj));
        }

        /// <summary>
        /// Registers a type with a <see cref="Func{T, TemplateContext, Task{Object}}"/> to retrieve the given property's value.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="name">The name of the property.</param>
        /// <param name="accessor">The <see cref="Func{T, TemplateContext, Task{Object}}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, string name, Func<T, TemplateContext, Task<TResult>> accessor)
        {
            strategy.Register(typeof(T), name, new AsyncDelegateAccessor<T, TResult>((obj, propertyName, ctx) => accessor(obj, ctx)));
        }

        /// Registers a type with a <see cref="Func{T, Object}"/> to retrieve the property specified.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="name">The name of the property.</param>
        /// <param name="accessor">The <see cref="Func{T, Object}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, string name, Func<T, TResult> accessor)
        {
            Register<T, TResult>(strategy, name, (obj, ctx) => accessor(obj));
        }

        /// <summary>
        /// Registers a type with a <see cref="Func{T, TemplateContext, TResult}"/> to retrieve the property specified.
        /// </summary>
        /// <param name="type">The type to register.</param>
        /// <param name="name">The name of the property.</param>
        /// <param name="accessor">The <see cref="Func{T, TemplateContext, TResult}"/> instance used to retrieve the value.</param>
        public static void Register<T, TResult>(this IMemberAccessStrategy strategy, string name, Func<T, TemplateContext, TResult> accessor)
        {
            strategy.Register(typeof(T), name, new DelegateAccessor<T, TResult>((obj, propertyName, ctx) => accessor(obj, ctx)));
        }
    }
}
