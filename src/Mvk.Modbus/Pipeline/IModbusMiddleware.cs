using System.Threading;
using System.Threading.Tasks;

namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Middleware обработки запросов к Modbus-устройствам (аналог middleware в ASP.NET).
    /// </summary>
    public interface IModbusMiddleware
    {
        /// <summary>
        /// Обрабатывает запрос; для передачи управления дальше вызывает <paramref name="next"/>.
        /// </summary>
        /// <param name="context">Контекст запроса.</param>
        /// <param name="next">Следующий обработчик пайплайна.</param>
        /// <param name="cancellationToken">Токен отмены (может быть заменён на связанный, например таймаутом).</param>
        Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken);
    }
}
