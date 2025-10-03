// Private Message Parser Implementation
// This class demonstrates the enhanced parsing logic for private messages

namespace ChatAppTcpProject
{
    public static class PrivateMessageParser
    {
        /// <summary>
        /// Parses private message command and extracts target user and message
        /// </summary>
        /// <param name="input">The input string starting with "/w "</param>
        /// <returns>Tuple containing target user and message, or null if parsing fails</returns>
        public static (string targetUser, string message)? ParsePrivateMessage(string input)
        {
            if (!input.StartsWith("/w "))
                return null;

            string remainingText = input.Substring(3).Trim();
            
            string targetUser;
            string message;
            
            if (remainingText.StartsWith("{"))
            {
                // Curly brace nickname format: /w {nickname} message
                int endBraceIndex = remainingText.IndexOf('}', 1);
                if (endBraceIndex == -1)
                    return null;
                
                targetUser = remainingText.Substring(1, endBraceIndex - 1).Trim();
                message = remainingText.Substring(endBraceIndex + 1).Trim();
            }
            else
            {
                // Regular format: /w target message
                int firstSpaceIndex = remainingText.IndexOf(' ');
                if (firstSpaceIndex == -1)
                    return null;
                
                targetUser = remainingText.Substring(0, firstSpaceIndex).Trim();
                message = remainingText.Substring(firstSpaceIndex + 1).Trim();
            }
            
            if (string.IsNullOrEmpty(targetUser) || string.IsNullOrEmpty(message))
                return null;

            return (targetUser, message);
        }

        /// <summary>
        /// Validates if the input is a valid private message command
        /// </summary>
        /// <param name="input">The input string to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidPrivateMessageCommand(string input)
        {
            return ParsePrivateMessage(input) != null;
        }

        /// <summary>
        /// Gets the usage message for private message command
        /// </summary>
        /// <returns>Usage message string</returns>
        public static string GetUsageMessage()
        {
            return "Usage: /w <user> <message> or /w {<user>} <message>";
        }
    }
}
