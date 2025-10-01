// Unit Tests for Private Message Parser
// This file contains test cases for the private message parsing functionality

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ChatAppTcpProject;

namespace ChatAppTcpProject.Tests
{
    [TestClass]
    public class PrivateMessageParserTests
    {
        [TestMethod]
        public void ParsePrivateMessage_CurlyBraceNickname_ReturnsCorrectValues()
        {
            // Arrange
            string input = "/w {John Doe} Hello there!";
            
            // Act
            var result = PrivateMessageParser.ParsePrivateMessage(input);
            
            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("John Doe", result.Value.targetUser);
            Assert.AreEqual("Hello there!", result.Value.message);
        }

        [TestMethod]
        public void ParsePrivateMessage_RegularNickname_ReturnsCorrectValues()
        {
            // Arrange
            string input = "/w John Hello there!";
            
            // Act
            var result = PrivateMessageParser.ParsePrivateMessage(input);
            
            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("John", result.Value.targetUser);
            Assert.AreEqual("Hello there!", result.Value.message);
        }

        [TestMethod]
        public void ParsePrivateMessage_ComplexNickname_ReturnsCorrectValues()
        {
            // Arrange
            string input = "/w {Dr. Mary-Jane Smith} How are you?";
            
            // Act
            var result = PrivateMessageParser.ParsePrivateMessage(input);
            
            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Dr. Mary-Jane Smith", result.Value.targetUser);
            Assert.AreEqual("How are you?", result.Value.message);
        }

        [TestMethod]
        public void ParsePrivateMessage_InvalidInput_ReturnsNull()
        {
            // Arrange
            string input = "/w John";
            
            // Act
            var result = PrivateMessageParser.ParsePrivateMessage(input);
            
            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ParsePrivateMessage_UnclosedBrace_ReturnsNull()
        {
            // Arrange
            string input = "/w {John Doe Hello there!";
            
            // Act
            var result = PrivateMessageParser.ParsePrivateMessage(input);
            
            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ParsePrivateMessage_EmptyMessage_ReturnsNull()
        {
            // Arrange
            string input = "/w {John Doe} ";
            
            // Act
            var result = PrivateMessageParser.ParsePrivateMessage(input);
            
            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void IsValidPrivateMessageCommand_ValidInput_ReturnsTrue()
        {
            // Arrange
            string input = "/w {John Doe} Hello there!";
            
            // Act
            bool result = PrivateMessageParser.IsValidPrivateMessageCommand(input);
            
            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsValidPrivateMessageCommand_InvalidInput_ReturnsFalse()
        {
            // Arrange
            string input = "/w John";
            
            // Act
            bool result = PrivateMessageParser.IsValidPrivateMessageCommand(input);
            
            // Assert
            Assert.IsFalse(result);
        }
    }
}
