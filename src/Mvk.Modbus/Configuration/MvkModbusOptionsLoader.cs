using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Загрузка конфигурации библиотеки из JSON-файла без использования DI-контейнера.
    /// </summary>
    public static class MvkModbusOptionsLoader
    {
        /// <summary>
        /// Имя секции конфигурации по умолчанию.
        /// </summary>
        public const string DefaultSectionName = "MvkModbus";

        private static readonly MvkModbusOptionsValidator Validator = new();

        /// <summary>
        /// Создаёт конфигурацию из JSON-файла.
        /// </summary>
        /// <param name="jsonFilePath">Путь к JSON-файлу.</param>
        public static IConfigurationRoot BuildConfiguration(string jsonFilePath)
        {
            if (string.IsNullOrWhiteSpace(jsonFilePath))
            {
                throw new ArgumentException("Не задан путь к файлу конфигурации.", nameof(jsonFilePath));
            }

            string fullPath = Path.GetFullPath(jsonFilePath);
            if (!File.Exists(fullPath))
            {
                throw new MvkModbusException(
                    $"Файл конфигурации не найден: '{fullPath}'.",
                    MvkErrorKind.Configuration);
            }

            return new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(fullPath)!)
                .AddJsonFile(Path.GetFileName(fullPath), optional: false, reloadOnChange: false)
                .Build();
        }

        /// <summary>
        /// Загружает и проверяет секцию <c>MvkModbus</c> из JSON-файла.
        /// </summary>
        /// <param name="jsonFilePath">Путь к JSON-файлу.</param>
        /// <param name="sectionName">Имя секции конфигурации.</param>
        /// <exception cref="MvkModbusException">
        /// Секция не найдена или конфигурация некорректна (<see cref="MvkErrorKind.Configuration"/>).
        /// </exception>
        public static MvkModbusOptions LoadFromJsonFile(string jsonFilePath, string sectionName = DefaultSectionName)
        {
            IConfigurationRoot configuration = BuildConfiguration(jsonFilePath);
            return LoadFromConfiguration(configuration, sectionName);
        }

        /// <summary>
        /// Загружает и проверяет секцию <c>MvkModbus</c> из готовой конфигурации.
        /// </summary>
        /// <param name="configuration">Конфигурация (appsettings, JSON-файл и т.п.).</param>
        /// <param name="sectionName">Имя секции конфигурации.</param>
        /// <exception cref="MvkModbusException">
        /// Секция не найдена или конфигурация некорректна (<see cref="MvkErrorKind.Configuration"/>).
        /// </exception>
        public static MvkModbusOptions LoadFromConfiguration(
            IConfiguration configuration,
            string sectionName = DefaultSectionName)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            if (string.IsNullOrWhiteSpace(sectionName))
            {
                throw new ArgumentException("Не задано имя секции конфигурации.", nameof(sectionName));
            }

            MvkModbusOptions? options = configuration.GetSection(sectionName).Get<MvkModbusOptions>();
            if (options is null)
            {
                throw new MvkModbusException(
                    $"Секция '{sectionName}' не найдена в конфигурации.",
                    MvkErrorKind.Configuration);
            }

            ValidateOptionsResult result = Validator.Validate(null, options);
            if (result.Failed)
            {
                throw new MvkModbusException(
                    $"Некорректная конфигурация '{sectionName}': {string.Join(" ", result.Failures)}",
                    MvkErrorKind.Configuration);
            }

            return options;
        }
    }
}
