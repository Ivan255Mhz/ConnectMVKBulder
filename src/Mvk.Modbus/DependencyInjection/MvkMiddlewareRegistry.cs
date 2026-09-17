using System;
using System.Collections.Generic;
using Mvk.Modbus.Pipeline;

namespace Mvk.Modbus.DependencyInjection
{
    /// <summary>
    /// Реестр пользовательских middleware: имя из секции <c>Pipeline</c> → тип middleware.
    /// </summary>
    public sealed class MvkMiddlewareRegistry
    {
        private readonly Dictionary<string, Type> types = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Регистрирует тип middleware под именем.
        /// </summary>
        /// <param name="name">Имя для секции <c>Pipeline</c> конфигурации.</param>
        /// <param name="middlewareType">Тип middleware с конструктором, разрешаемым через DI.</param>
        public void Add(string name, Type middlewareType)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Не задано имя middleware.", nameof(name));
            }

            ArgumentNullException.ThrowIfNull(middlewareType);

            if (!typeof(IModbusMiddleware).IsAssignableFrom(middlewareType))
            {
                throw new MvkModbusException(
                    $"Тип '{middlewareType.Name}' не реализует {nameof(IModbusMiddleware)}.",
                    MvkErrorKind.Configuration);
            }

            types[name.Trim()] = middlewareType;
        }

        /// <summary>
        /// Ищет тип middleware по имени.
        /// </summary>
        /// <param name="name">Имя из секции <c>Pipeline</c>.</param>
        /// <param name="middlewareType">Найденный тип.</param>
        public bool TryGetType(string name, out Type middlewareType)
        {
            return types.TryGetValue(name, out middlewareType!);
        }
    }
}
