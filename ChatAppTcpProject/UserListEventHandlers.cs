// User List Event Handlers for Private Message Functionality
// This class contains the event handlers for user list interactions

using System;
using System.Windows;
using System.Windows.Input;

namespace ChatAppTcpProject
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Handles double-click event on user list to initiate private message
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">Mouse button event arguments</param>
        private void UsersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UsersList.SelectedItem is string selectedUser)
            {
                string currentUser = NicknameTextBox?.Text?.Trim() ?? "";
                if (selectedUser.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    AppendSystem("Cannot send private message to yourself.");
                    return;
                }
                
                // Focus message input and set up PM
                MessageInput.Focus();
                MessageInput.Text = $"/w {{{selectedUser}}} ";
                MessageInput.CaretIndex = MessageInput.Text.Length;
            }
        }

        /// <summary>
        /// Handles context menu click for sending private message
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">Routed event arguments</param>
        private void SendPMContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selectedUser)
            {
                string currentUser = NicknameTextBox?.Text?.Trim() ?? "";
                if (selectedUser.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    AppendSystem("Cannot send private message to yourself.");
                    return;
                }
                
                MessageInput.Focus();
                MessageInput.Text = $"/w {{{selectedUser}}} ";
                MessageInput.CaretIndex = MessageInput.Text.Length;
            }
        }

        /// <summary>
        /// Handles context menu click for copying username to clipboard
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">Routed event arguments</param>
        private void CopyUsernameContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selectedUser)
            {
                Clipboard.SetText(selectedUser);
                AppendSystem($"Username '{selectedUser}' copied to clipboard.");
            }
        }
    }
}
