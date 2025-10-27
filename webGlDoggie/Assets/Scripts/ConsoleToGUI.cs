using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DebugStuff
{
    public class ConsoleToGUI : MonoBehaviour
    {
        [Header("Output")]
        [SerializeField] private Text consoleText;
        [SerializeField, Range(1, 200)] private int maxEntries = 80;
        [SerializeField] private int maxCharacters = 8000;
        [SerializeField] private bool includeStackTrace;

        [Header("Log Filters")]
        [SerializeField] private bool showLogs = true;
        [SerializeField] private bool showWarnings = true;
        [SerializeField] private bool showErrors = true;

        private readonly Queue<string> entries = new Queue<string>();
        private readonly StringBuilder builder = new StringBuilder(2048);
        private int currentCharacterCount;
        private bool isDirty;

        private void OnEnable()
        {
            if (consoleText == null)
            {
                Debug.LogWarning($"{nameof(ConsoleToGUI)} requires a {nameof(Text)} reference to display logs.", this);
            }

            Application.logMessageReceived += HandleLog;
            RefreshOutput();
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void Update()
        {
            if (!isDirty)
            {
                return;
            }

            RefreshOutput();
        }

        private void HandleLog(string message, string stackTrace, LogType type)
        {
            if (!ShouldDisplay(type))
            {
                return;
            }

            string entry = includeStackTrace && !string.IsNullOrEmpty(stackTrace)
                ? $"{message}\n{stackTrace}"
                : message;

            entries.Enqueue(entry);
            currentCharacterCount += entry.Length + 1;

            while ((maxEntries > 0 && entries.Count > maxEntries) || (maxCharacters > 0 && currentCharacterCount > maxCharacters))
            {
                if (entries.Count == 0)
                {
                    break;
                }

                string removed = entries.Dequeue();
                currentCharacterCount -= removed.Length + 1;
            }

            isDirty = true;
        }

        private bool ShouldDisplay(LogType type)
        {
            switch (type)
            {
                case LogType.Log:
                    return showLogs;
                case LogType.Warning:
                    return showWarnings;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return showErrors;
                default:
                    return true;
            }
        }

        private void RefreshOutput()
        {
            if (consoleText == null)
            {
                isDirty = false;
                return;
            }

            builder.Clear();

            foreach (string entry in entries)
            {
                builder.AppendLine(entry);
            }

            consoleText.text = builder.ToString();
            isDirty = false;
        }

        private void OnValidate()
        {
            maxEntries = Mathf.Clamp(maxEntries, 1, 200);
            if (maxCharacters < 0)
            {
                maxCharacters = 0;
            }
        }
    }
}
