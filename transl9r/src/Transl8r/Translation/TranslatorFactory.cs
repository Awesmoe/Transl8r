using System;
using Transl8r.Config;

namespace Transl8r.Translation;

internal static class TranslatorFactory
{
    public static ITranslator Build(AppConfig cfg) => cfg.Translator switch
    {
        "deepl" => new DeepLTranslator(cfg.DeeplApiKey),
        "server" => new ServerTranslator(cfg.ServerUrl, cfg.ServerModel),
        "argos" => throw new NotSupportedException(
            "The offline 'argos' translator isn't ported to C#. Choose 'deepl' or " +
            "'server', or use the 'vlm-direct' OCR backend (no separate translator)."),
        _ => throw new NotSupportedException($"Unknown translator '{cfg.Translator}'."),
    };
}
