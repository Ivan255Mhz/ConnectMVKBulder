using Mvk.Modbus.Configuration;

namespace Mvk.Modbus.Tests
{
    /// <summary>
    /// Фабрика параметров устройства для тестов.
    /// </summary>
    internal static class TestDeviceSettings
    {
        public static MvkDeviceSettings Create(
            string id = "mvk-1",
            string host = "127.0.0.1",
            int port = 502,
            int connectTimeoutMs = 2000,
            int readTimeoutMs = 1000,
            int writeTimeoutMs = 2000,
            int maxRetries = 0,
            int retryDelayMs = 0,
            bool enabled = true,
            int maxConcurrentRequests = 1,
            int idleDisconnectSeconds = 0,
            int reconnectBaseDelayMs = 100,
            int reconnectMaxDelayMs = 1000,
            bool requireEvenAddressAndQuantity = false,
            MvkWritePolicy writePolicy = MvkWritePolicy.AllowAll,
            bool allowConfigurationWrite = false,
            ushort protectedStartAddress = 0,
            ushort protectedEndAddress = 0)
        {
            return new MvkDeviceSettings
            {
                Id = id,
                Host = host,
                Port = port,
                SlaveId = 1,
                ByteOrder = MvkByteOrder.BigEndian,
                Enabled = enabled,
                MaxConcurrentRequests = maxConcurrentRequests,
                AllowConfigurationWrite = allowConfigurationWrite,
                ConnectTimeoutMs = connectTimeoutMs,
                ReadTimeoutMs = readTimeoutMs,
                WriteTimeoutMs = writeTimeoutMs,
                MaxRetries = maxRetries,
                RetryDelayMs = retryDelayMs,
                ReconnectBaseDelayMs = reconnectBaseDelayMs,
                ReconnectMaxDelayMs = reconnectMaxDelayMs,
                IdleDisconnectSeconds = idleDisconnectSeconds,
                WritePolicy = writePolicy,
                ProtectedWriteStartAddress = protectedStartAddress,
                ProtectedWriteEndAddress = protectedEndAddress,
                RequireEvenAddressAndQuantity = requireEvenAddressAndQuantity,
                Tags = new Dictionary<string, string>()
            };
        }
    }
}
