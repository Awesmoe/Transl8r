namespace Transl8r.Translation;

/// <summary>Translates Japanese text to English.</summary>
internal interface ITranslator
{
    string Translate(string text);
}
