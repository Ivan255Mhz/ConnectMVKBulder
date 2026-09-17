using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mvk.Testing
{
    /// <summary>
    /// TCP-сервер, который принимает соединения и молчит: используется для проверки
    /// таймаутов чтения (устройство не отвечает).
    /// </summary>
    public sealed class SilentTcpServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly List<TcpClient> clients = [];
        private readonly Task acceptTask;

        /// <summary>
        /// Порт, на котором слушает сервер.
        /// </summary>
        public int Port { get; }

        private SilentTcpServer(TcpListener listener)
        {
            this.listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            acceptTask = AcceptLoopAsync();
        }

        /// <summary>
        /// Запускает молчащий сервер.
        /// </summary>
        /// <param name="port">TCP-порт; 0 — свободный порт, выбранный операционной системой.</param>
        public static SilentTcpServer Start(int port = 0)
        {
            TcpListener listener = new(IPAddress.Loopback, port);
            listener.Start();
            return new SilentTcpServer(listener);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync().ConfigureAwait(false);

            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
            }

            lock (clients)
            {
                foreach (TcpClient client in clients)
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }

                clients.Clear();
            }

            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(cancellation.Token).ConfigureAwait(false);
                    lock (clients)
                    {
                        clients.Add(client);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
            }
        }
    }
}
