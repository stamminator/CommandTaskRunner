﻿using System;
using System.Linq;
using System.Xml.Linq;
using CommandTaskRunner;
using Microsoft.VisualStudio.TextManager.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectTaskRunner.Helpers
{
    internal class BindingsPersister
    {
        private const string BindingsName = CommandTaskRunner.Constants.ELEMENT_NAME;
        private TaskRunnerProvider _provider;

        public BindingsPersister(TaskRunnerProvider provider)
        {
            _provider = provider;
        }

        public string Load(string configPath)
        {
            IVsTextView configTextView = TextViewUtil.FindTextViewFor(configPath);
            ITextUtil textUtil;

            if (configTextView != null)
            {
                textUtil = new VsTextViewTextUtil(configTextView);
            }
            else
            {
                textUtil = new FileTextUtil(configPath);
            }

            string fileText = textUtil.ReadAllText();
            var body = JObject.Parse(fileText);

            var bindings = body[BindingsName] as JObject;

            if (bindings != null)
            {
                var bindingsElement = XElement.Parse("<binding />");

                foreach (JProperty property in bindings.Properties())
                {
                    string[] tasks = property.Value.Values<string>().ToArray();

                    for(int i = 0; i < tasks.Length; ++i)
                    {
                        tasks[i] = _provider.GetDynamicName(tasks[i]);
                    }

                    bindingsElement.SetAttributeValue(property.Name, string.Join(",", tasks));
                }

                return bindingsElement.ToString();
            }

            return "<binding />";
        }

        public bool Save(string configPath, string bindingsXml)
        {
            bindingsXml = bindingsXml.Replace("\u200B", string.Empty);

            var bindingsXmlObject = XElement.Parse(bindingsXml);
            var bindingsXmlBody = JObject.Parse(@"{}");
            bool anyAdded = false;

            foreach (XAttribute attribute in bindingsXmlObject.Attributes())
            {
                var type = new JArray();
                bindingsXmlBody[attribute.Name.LocalName] = type;
                string[] tasks = attribute.Value.Split(',');

                foreach (string task in tasks)
                {
                    anyAdded = true;
                    type.Add(task.Trim());
                }
            }

            IVsTextView configTextView = TextViewUtil.FindTextViewFor(configPath);
            ITextUtil textUtil;

            if (configTextView != null)
            {
                textUtil = new VsTextViewTextUtil(configTextView);
            }
            else
            {
                textUtil = new FileTextUtil(configPath);
            }

            string currentContents = textUtil.ReadAllText();
            var fileModel = JObject.Parse(currentContents);
            bool commaRequired = fileModel.Properties().Any();
            JProperty currentBindings = fileModel.Property(BindingsName);
            bool insert = currentBindings == null;
            fileModel[BindingsName] = bindingsXmlBody;

            JProperty property = fileModel.Property(BindingsName);
            string bindingText = property.ToString(Formatting.None);
            textUtil.Reset();

            if (!anyAdded)
            {
                insert = false;
            }

            if (insert)
            {
                if (commaRequired)
                {
                    bindingText = ", " + bindingText;
                }

                string line;
                int lineNumber = 0, candidateLine = -1, lastBraceIndex = -1, characterCount = 0;
                while (textUtil.TryReadLine(out line))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        int brace = line.LastIndexOf('}');
                        if (brace >= 0)
                        {
                            lastBraceIndex = brace;
                            candidateLine = lineNumber;
                        }
                    }

                    characterCount += line?.Length ?? 0;
                    ++lineNumber;
                }

                if (candidateLine >= 0 && lastBraceIndex >= 0)
                {
                    if (textUtil.Insert(new Range {LineNumber = candidateLine, LineRange = new LineRange {Start = lastBraceIndex, Length = bindingText.Length}}, bindingText, true))
                    {
                        textUtil.FormatRange(new LineRange { Start = characterCount, Length = bindingText.Length });
                        return true;
                    }
                }

                return false;
            }

            int bindingsIndex = currentContents.IndexOf(@"""" + BindingsName + @"""", StringComparison.Ordinal);
            int closeBindingsBrace = currentContents.IndexOf('}', bindingsIndex) + 1;
            int length = closeBindingsBrace - bindingsIndex;

            int startLine, startLineOffset, endLine, endLineOffset;
            textUtil.GetExtentInfo(bindingsIndex, length, out startLine, out startLineOffset, out endLine, out endLineOffset);

            if (!anyAdded)
            {
                int previousComma = currentContents.LastIndexOf(',', bindingsIndex);
                int tail = 0;

                if (previousComma > -1)
                {
                    tail += bindingsIndex - previousComma;
                    textUtil.GetExtentInfo(previousComma, length, out startLine, out startLineOffset, out endLine, out endLineOffset);
                }

                if (textUtil.Delete(new Range {LineNumber = startLine, LineRange = new LineRange {Start = startLineOffset, Length = length + tail}}))
                {
                    textUtil.Reset();
                    textUtil.FormatRange(new LineRange {Start = bindingsIndex, Length = 2});
                    return true;
                }

                return false;
            }

            bool success = textUtil.Replace(new Range {LineNumber = startLine, LineRange = new LineRange {Start = startLineOffset, Length = length}}, bindingText);

            if (success)
            {
                textUtil.FormatRange(new LineRange {Start = bindingsIndex, Length = bindingText.Length});
            }

            return success;
        }
    }

    public static class TextUtilExtensions2
    {
        public static void GetExtentInfo(this ITextUtil textUtil, int startIndex, int length, out int startLine, out int startLineOffset, out int endLine, out int endLineOffset)
        {
            textUtil.Reset();
            int lineNumber = 0, charCount = 0, lineCharCount = 0;
            string line;
            while (textUtil.TryReadLine(out line) && charCount < startIndex)
            {
                ++lineNumber;
                charCount += line.Length;
                lineCharCount = line.Length;
            }

            //We passed the line we want to be on, so back up
            int positionAtEndOfPreviousLine = charCount - lineCharCount;
            startLine = lineNumber - 1;
            startLineOffset = startIndex - positionAtEndOfPreviousLine;


            while (textUtil.TryReadLine(out line) && charCount < startIndex + length)
            {
                ++lineNumber;
                charCount += line.Length;
                lineCharCount = line.Length;
            }

            if (line != null)
            {
                positionAtEndOfPreviousLine = charCount - lineCharCount;
                endLineOffset = startIndex + length - positionAtEndOfPreviousLine;
            }
            else
            {
                endLineOffset = lineCharCount;
            }

            endLine = lineNumber - 1;
        }
    }
}