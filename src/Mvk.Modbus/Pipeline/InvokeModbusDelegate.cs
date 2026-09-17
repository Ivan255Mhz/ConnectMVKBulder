using System.Threading;
using System.Threading.Tasks;

namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Обработчик запроса в пайплайне. Каждый middleware получает следующий обработчик
    /// и может выполнить действие до/после него, подменить токен отмены, изменить ответ
    /// или вовсе не вызывать следующий обработчик (short-circuit).
    /// </summary>
    /// <param name="context">Контекст запроса.</param>
    /// <param name="cancellationToken">Токен отмены для вложенных обработчиков.</param>
    public delegate Task InvokeModbusDelegate(ModbusRequestContext context, CancellationToken cancellationToken);
}
