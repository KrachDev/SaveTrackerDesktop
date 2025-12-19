using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC.Steam
{
    /// <summary>
    /// Parser for Valve Data Format (VDF) files used by Steam.
    /// VDF is a text-based key-value format similar to JSON but with different syntax.
    /// </summary>
    public static class VdfParser
    {
        /// <summary>
        /// Parses a VDF file from disk.
        /// </summary>
        public static VdfNode? ParseFile(string path)
        {
            if (!File.Exists(path))
            {
                DebugConsole.WriteWarning($"[VdfParser] File not found: {path}");
                return null;
            }

            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                return Parse(content);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"[VdfParser] Failed to read file: {path}");
                return null;
            }
        }

        /// <summary>
        /// Parses VDF content from a string.
        /// </summary>
        public static VdfNode? Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                var tokenizer = new VdfTokenizer(content);
                return ParseNode(tokenizer);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "[VdfParser] Parse error");
                return null;
            }
        }

        private static VdfNode? ParseNode(VdfTokenizer tokenizer)
        {
            var root = new VdfNode();

            while (tokenizer.HasMore())
            {
                string? key = tokenizer.ReadString();
                if (key == null)
                    break;

                // Check if this is a nested object or a value
                string? nextToken = tokenizer.PeekNextToken();

                if (nextToken == "{")
                {
                    // Consume the opening brace
                    tokenizer.ReadToken();

                    // Parse nested node
                    var childNode = ParseNestedNode(tokenizer);
                    root.Children[key] = childNode;
                }
                else if (nextToken == "}")
                {
                    // End of current block, don't consume
                    break;
                }
                else
                {
                    // It's a value
                    string? value = tokenizer.ReadString();
                    if (value != null)
                    {
                        root.Values[key] = value;
                    }
                }
            }

            return root;
        }

        private static VdfNode ParseNestedNode(VdfTokenizer tokenizer)
        {
            var node = new VdfNode();

            while (tokenizer.HasMore())
            {
                string? nextToken = tokenizer.PeekNextToken();

                if (nextToken == "}")
                {
                    // Consume closing brace and exit
                    tokenizer.ReadToken();
                    break;
                }

                string? key = tokenizer.ReadString();
                if (key == null)
                    break;

                nextToken = tokenizer.PeekNextToken();

                if (nextToken == "{")
                {
                    // Consume opening brace
                    tokenizer.ReadToken();

                    // Recursively parse child
                    var childNode = ParseNestedNode(tokenizer);
                    node.Children[key] = childNode;
                }
                else
                {
                    // It's a value
                    string? value = tokenizer.ReadString();
                    if (value != null)
                    {
                        node.Values[key] = value;
                    }
                }
            }

            return node;
        }
    }

    /// <summary>
    /// Represents a node in a VDF document.
    /// A node can have both simple key-value pairs and nested child nodes.
    /// </summary>
    public class VdfNode
    {
        /// <summary>
        /// Simple key-value pairs in this node.
        /// </summary>
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Nested child nodes.
        /// </summary>
        public Dictionary<string, VdfNode> Children { get; } = new Dictionary<string, VdfNode>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets a value by key, or null if not found.
        /// </summary>
        public string? GetValue(string key)
        {
            return Values.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Gets a child node by key, or null if not found.
        /// </summary>
        public VdfNode? GetChild(string key)
        {
            return Children.TryGetValue(key, out var child) ? child : null;
        }
    }

    /// <summary>
    /// Simple tokenizer for VDF format.
    /// </summary>
    internal class VdfTokenizer
    {
        private readonly string _content;
        private int _position;

        public VdfTokenizer(string content)
        {
            _content = content;
            _position = 0;
        }

        public bool HasMore()
        {
            SkipWhitespaceAndComments();
            return _position < _content.Length;
        }

        public string? PeekNextToken()
        {
            int savedPosition = _position;
            SkipWhitespaceAndComments();

            if (_position >= _content.Length)
            {
                _position = savedPosition;
                return null;
            }

            char c = _content[_position];
            _position = savedPosition;

            if (c == '{' || c == '}')
                return c.ToString();

            return "string"; // Indicates it's a quoted string
        }

        public string? ReadToken()
        {
            SkipWhitespaceAndComments();

            if (_position >= _content.Length)
                return null;

            char c = _content[_position];

            if (c == '{' || c == '}')
            {
                _position++;
                return c.ToString();
            }

            return ReadString();
        }

        public string? ReadString()
        {
            SkipWhitespaceAndComments();

            if (_position >= _content.Length)
                return null;

            char c = _content[_position];

            if (c == '"')
            {
                // Quoted string
                _position++; // Skip opening quote
                var sb = new StringBuilder();

                while (_position < _content.Length)
                {
                    c = _content[_position];

                    if (c == '"')
                    {
                        _position++; // Skip closing quote
                        break;
                    }
                    else if (c == '\\' && _position + 1 < _content.Length)
                    {
                        // Escape sequence
                        _position++;
                        char escaped = _content[_position];
                        switch (escaped)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default: sb.Append(escaped); break;
                        }
                        _position++;
                    }
                    else
                    {
                        sb.Append(c);
                        _position++;
                    }
                }

                return sb.ToString();
            }
            else if (c != '{' && c != '}')
            {
                // Unquoted token (until whitespace or special char)
                var sb = new StringBuilder();

                while (_position < _content.Length)
                {
                    c = _content[_position];
                    if (char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '"')
                        break;

                    sb.Append(c);
                    _position++;
                }

                return sb.Length > 0 ? sb.ToString() : null;
            }

            return null;
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _content.Length)
            {
                char c = _content[_position];

                if (char.IsWhiteSpace(c))
                {
                    _position++;
                }
                else if (c == '/' && _position + 1 < _content.Length && _content[_position + 1] == '/')
                {
                    // Line comment
                    while (_position < _content.Length && _content[_position] != '\n')
                    {
                        _position++;
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }
}
