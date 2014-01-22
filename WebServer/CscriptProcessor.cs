using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WebServer
{
    /* An implementation of an IScriptProcessor for processing 
     * CGI-BIN like scripts */
    public class CscriptProcessor : IScriptProcessor
    {
        private ICodeCompiler _compiler;
        private CompilerParameters _parameters;

        /* this will represent the class that we are going to be compiling 
         * and executing, the method body of {0} will be replaced with the 
         * contents of the script file that we are executing */
        private const string _classTemplate = "using System;" +
            "namespace Server {" +
                "public class Executor {" +
                    "public void Execute(System.IO.StringWriter wout, System.Collections.Generic.Dictionary<string, string> request) {" +
                        "{0}" +
                    "}" +
                "}" +
            "}";

        /* setup the compiler on construction to prevent the overhead of 
         * having to do that for each call */
        public CscriptProcessor()
        {
            CSharpCodeProvider provider = new CSharpCodeProvider();
            _compiler = provider.CreateCompiler();

            _parameters = new CompilerParameters();
            
            /* this processor will only support the System namespace */
            _parameters.ReferencedAssemblies.Add("system.dll");

            /* don't compile this to the disk, just leave the assembly in memory */
            _parameters.GenerateInMemory = true;

            /* build a library */
            _parameters.CompilerOptions = "/t:library";
        }

        public ScriptResult ProcessScript(string path, IDictionary<string, string> requestParameters)
        {
            StringBuilder scriptBody = new StringBuilder();
         
            /* read the contents of the file into a string 
             * builder line by line to create a single 
             * string that represents the script */
            using (FileStream fs = File.OpenRead(path))
            {
                StreamReader reader = new StreamReader(fs);
                string line = null;
                while ((line = reader.ReadLine()) != null)
                {
                    scriptBody.Append(line);
                }
            }

            /* combine the script string with the class template in order to create something 
             * that can be compiled. NOTE that the class template provides the wout and 
             * request variables */
            string source = _classTemplate.Replace("{0}", scriptBody.ToString());

            /* compile the generated source */
            CompilerResults result = _compiler.CompileAssemblyFromSource(_parameters, source);

            /* if the source didn't compile, generate an html document that lists the 
             * compilation errors and return a failed script result with that html
             * document as the result */
            if (result.Errors.Count > 0)
            {
                StringBuilder errorBody = new StringBuilder();
                errorBody.Append("<html><body>");
                errorBody.Append("<h1>Script Compilation Errors</h1>");
                errorBody.Append("<p>The following errors occurred processing the requested resource</p>");
                errorBody.Append("<ul>");
                foreach (CompilerError error in result.Errors)
                {
                    errorBody.Append(string.Format("<li>{0}:{1} - Error: {2}</li>", error.Line, error.Column, error.ErrorText));
                }
                errorBody.Append("</ul>");
                errorBody.Append("</body></html>");

                /* the script result with the list of errors as the result */
                return new ScriptResult()
                {
                    Error = true,
                    Result = errorBody.ToString()
                };
            }
            
            /* if the class compiled, use reflection to get the assembly and 
             * instantiate the class */
            System.Reflection.Assembly codeAssembly = result.CompiledAssembly;
            object instance = codeAssembly.CreateInstance("Server.Executor");

            /* get an instance of the type of the class (i.e. Server.Executor) */
            Type instanceType = instance.GetType();

            /* get an instance of the method that we want to invoke 
             * (i.e. Execute(StringWriter wout, Dictionary<string, string> request)) */
            MethodInfo executionMethod = instanceType.GetMethod("Execute", new Type[] { typeof(StringWriter), typeof(Dictionary<string, string>) });

            /* now we want to invoke the method on the instance of the Executor
             * that we created above */
            try
            {
                /* create a string writer to send in as wout */
                StringWriter s = new StringWriter();

                /* invoke the method with the string writer and request dictionary */
                executionMethod.Invoke(instance, new object[] { s, requestParameters });

                /* return a script result with the contents of the string writer which 
                 * should be the HTML (or whatever) that was produced by the script
                 * and written to wout */
                return new ScriptResult() 
                { 
                    Error = false, 
                    Result = s.ToString() 
                };
            }
            catch (Exception e)
            {
                /* if the method cannot be invoked (i.e. runtime error) send back 
                 * a failed result with the runtime error */
                return new ScriptResult()
                {
                    Error = true,
                    Result = string.Format("<html><body><h1>Runtime Error</h1><p>The following runtime error occurred: {0}</p>", 
                        e.InnerException.Message)
                };
            }
        }
    }
}
