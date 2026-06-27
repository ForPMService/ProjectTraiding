using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Выбирает обработчик под задачу по виду данных, рынку и интервалу. Перебирает
    /// зарегистрированные обработчики и возвращает первый подходящий, либо null, если вид не
    /// поддержан (невозможная пара вид/рынок — например, статистика заявок по фьючерсам — до
    /// исполнения доходить не должна, её отсекает валидатор на создании).
    /// </summary>
    public sealed class LoadHandlerDispatcher
    {
        private readonly IReadOnlyList<ILoadHandler> _handlers;

        public LoadHandlerDispatcher(IEnumerable<ILoadHandler> handlers)
        {
            _handlers = handlers.ToList();
        }

        public ILoadHandler? Resolve(MoexLoadTask task)
        {
            foreach (ILoadHandler handler in _handlers)
            {
                if (handler.CanHandle(task))
                    return handler;
            }
            return null;
        }
    }
}
