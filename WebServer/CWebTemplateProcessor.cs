using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebServer
{
    public class CWebTemplateProcessor : IScriptProcessor
    {
        public CWebTemplateProcessor() {

        }

        public ScriptResult ProcessScript(string path, IDictionary<string, string> requestParameters)
        {
            string fileContents = String.Join(" ", File.ReadAllLines(path));
            int length = fileContents.Length;

            List<string> codes = new List<string>();
            
            int last = 0;

            for (int i = 0; i < length; i++) {

                // Parse out code blocks
                if (fileContents.ElementAt(i) ==  '{')
                {
                    // Get HTML up to this point
                    string html = fileContents.Substring(last, i - last);
                    codes.Add(string.Format(@"wout.WriteLine(""{0}"");", html));

                    // Find matching brace and add code
                    int count = 1;
                    int j = i;
                    while(count > 0)
                    {
                        j++;
                        if (fileContents.ElementAt(j) == '{')
                        {
                            count++;
                        }
                        else if (fileContents.ElementAt(j) == '}')
                        {
                            count--;
                        }
                    }

                    string code = fileContents.Substring(i + 1, j - i - 1); 
                    codes.Add(code);
                    
                    // Update variables
                    i = j;
                    last = i + 1;
                }

                // Parse out @{}
                if (i < length - 1 && fileContents.Substring(i, 2) == "@{")
                {
                    // Get HTML up to this point
                    string html = fileContents.Substring(last, i - last);
                    codes.Add(string.Format(@"wout.WriteLine(""{0}"");", html));

                    // Find matching brace and add code
                    int count = 1;
                    int j = i + 1;
                    while (count > 0)
                    {
                        j++;
                        if (fileContents.ElementAt(j) == '{')
                        {
                            count++;
                        }
                        else if (fileContents.ElementAt(j) == '}')
                        {
                            count--;
                        }
                    }

                    string code = fileContents.Substring(i + 2, j - i - 2);
                    codes.Add(String.Format("wout.WriteLine({0});", code));

                    // Update variables
                    i = j;
                    last = i + 1;
                }

            }

            string remaining = fileContents.Substring(last);
            codes.Add(String.Format(@"wout.WriteLine(""{0}"");", remaining));
            
            // This is jank.  But... :)
            string fileName = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".tmp";
            File.WriteAllLines(fileName, codes);

            // Use Cscript processor
            IScriptProcessor sp = new CscriptProcessor();
            ScriptResult result = sp.ProcessScript(fileName, requestParameters);

            // Delete the temp file
            File.Delete(fileName);

            // Return the result
            return result;

        }
    }
}
