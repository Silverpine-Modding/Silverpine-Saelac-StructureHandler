#nullable enable
using System;
using System.Text.RegularExpressions;

namespace StructureHandler;

// NPCFunction_GiveItem's installed LLM prompts/grammars. Quality deliberately
// permits the full game enum, as requested, rather than the NPC gift picker.
internal static class ItemCustomizationRules
{
    internal const int DescriptionWords = 30;
    internal const int LetterWords = 150;
    internal static int WordCount(string value) => Regex.Matches(value ?? "", @"\S+").Count;
    internal static string? ValidateText(string name, string description, string originalName, string originalDescription)
    {
        if (name != originalName && (string.IsNullOrWhiteSpace(name) ||
            !Regex.IsMatch(name, @"\A[A-Za-z0-9 .,!?'-]+\z")))
            return "Name must use the game's letters, numbers, spaces and punctuation (.,!?'-).";
        if (description != originalDescription &&
            (WordCount(description) > DescriptionWords || !Regex.IsMatch(description, @"\A[A-Za-z .,!?'-]*\z")))
            return "Description: at most 30 words; letters, spaces and punctuation (.,!?'-), as in the game's generation grammar.";
        return null;
    }
    internal static string? ValidateLetter(string sender, string message) =>
        !Regex.IsMatch(sender, @"\A[A-Za-z0-9 .,!?'-]*\z") ||
        !Regex.IsMatch(message, @"\A[A-Za-z0-9 .,!?'\r\n-]*\z") || WordCount(message) > LetterWords
            ? "Letter: at most 150 words; use letters, numbers, spaces, line breaks and punctuation (.,!?'-)." : null;
    internal static int NormalizeRotation(int degrees) => ((degrees / 45 % 8) + 8) % 8 * 45;
}
