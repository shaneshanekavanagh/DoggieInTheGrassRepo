
using UnityEngine;
using UnityEngine.UI;

namespace DebugStuff
{
    public class ConsoleToGUI : MonoBehaviour
    {
        //#if !UNITY_EDITOR
        static string myLog = "";
        private string output;
        private string stack;

        public Text ConsoleText;

        void OnEnable()
        {
            Application.logMessageReceived += Log;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= Log;
        }

        public void Log(string logString, string stackTrace, LogType type)
        {
            output = logString;
            stack = stackTrace;
            myLog = output + "\n" + myLog;
            if(myLog.Length > 5000)
            {
                myLog = myLog.Substring(0, 4000);
            }
        }

        private void Update()
        {
            ConsoleText.text = myLog;
        }
        //#endif
    }
}
