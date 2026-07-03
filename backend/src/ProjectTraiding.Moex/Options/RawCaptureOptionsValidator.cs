using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Options
{
    /// <summary>
    /// Проверка настроек захвата сырых ответов при запуске. При выключенном режиме
    /// (CaptureMode.Off) ничего не проверяется — захват не работает и реквизиты хранилища не
    /// нужны. При любом включённом режиме отсутствие адреса, бакета или ключей доступа
    /// обрывает старт понятной ошибкой, а не проявляется сбоем при первой записи в разгар
    /// загрузки. Значения ключей в журнал не пишутся.
    /// </summary>
    public static class RawCaptureOptionsValidator
    {
        public static void Validate(RawCaptureOptions options)
        {
            if (options.Mode == CaptureMode.Off)
                return;

            if (string.IsNullOrWhiteSpace(options.Endpoint))
                throw new InvalidOperationException(
                    "RawCapture:Endpoint обязателен при включённом режиме захвата.");

            if (string.IsNullOrWhiteSpace(options.Bucket))
                throw new InvalidOperationException(
                    "RawCapture:Bucket обязателен при включённом режиме захвата.");

            if (string.IsNullOrWhiteSpace(options.Region))
                throw new InvalidOperationException(
                    "RawCapture:Region обязателен при включённом режиме захвата.");

            if (string.IsNullOrWhiteSpace(options.AccessKey))
                throw new InvalidOperationException(
                    "RawCapture:AccessKey обязателен при включённом режиме захвата.");

            if (string.IsNullOrWhiteSpace(options.SecretKey))
                throw new InvalidOperationException(
                    "RawCapture:SecretKey обязателен при включённом режиме захвата.");
        }
    }
}
