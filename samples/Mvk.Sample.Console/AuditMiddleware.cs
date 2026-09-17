using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Pipeline;

namespace Mvk.Sample.Console;

/// <summary>
/// Пример пользовательского middleware: печатает каждый запрос к устройству
/// с результатом и длительностью.
/// </summary>
internal sealed class AuditMiddleware : IModbusMiddleware
{
    /// <inheritdoc/>
    public async Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();

        await next(context, cancellationToken);

        string result = context.Response?.IsSuccess == true
            ? "OK"
            : context.Response?.Error?.ErrorKind.ToString() ?? "нет ответа";

        System.Console.WriteLine(
            $"  [audit] {context.Settings.Id}: {context.Request.Operation} "
            + $"0x{context.Request.StartAddress:X4} x{context.Request.Quantity} — {result}, "
            + $"{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0} мс");
    }
}
