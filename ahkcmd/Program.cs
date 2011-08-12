using System;
using System.Collections.Generic;
using AhkWrapper;
using System.IO;

namespace AhkCmd
{
    class Program
    {

        // Always between 0 and 9
        static int HistoryIndex = 0;

        // Recently executed lines
        static List<string> RecentLines = new List<string>( 10 );

        static ConsoleColor CodeColor = ConsoleColor.Gray;  // TODO: try Yellow

        public static void Main(string[] args) {
            WriteIntro();

            // Title for the console window
            Console.Title = "AutoHotkey Command Line";

            InstallDll();

            // Start with a empty thread with the following ahk lines assumed
            //  #Persistent
            //  #NoTrayIcon
            AhkDll.ahktextdll( Properties.Resources.ScriptToLoad, "", "" );

            string userAction = null;
             do {
                userAction = UserGetAction();
                AhkDll.ahkExec( userAction );
                 
            } while (userAction != "die") ;

        }

        ///<summary>
        /// Write an intro message in two colors on one line
        ///</summary>
        public static void WriteIntro() {
            const byte maxLength = 79;
            string message = "AutoHotkey command line interface";
            int messageLength = (byte) message.Length;

            // Print half the line with fillers
            // We do this in a color for aesthetic purposes
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            for (int i = messageLength+2; i < maxLength; i+=4)
                Console.Write( "->" );

            // Now we write the main message padded with spaces
            // This gets the signature green color of AutoHotkey
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write( " " + message + " " );

            // Our last output fills the remainder of the line with fillers
            // This is done in a matching color to our first fillers
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            for (int i = messageLength+2; i < maxLength; i+=4)
                Console.Write( "<-" );

            // The color needs to be returned to the default so Write calls
            // aren't affected
            Console.ResetColor();

            // Insert a single newline character
            Console.WriteLine();
        }

        /// <summary>
        /// Recieve a command from the user
        /// </summary>
        /// <returns>AutoHotkey code to be executed</returns>
        public static string UserGetAction() {
            char inputChar = '-';
            ConsoleKeyInfo pressedKey = new ConsoleKeyInfo();
            string userInput = "";
            string textLine = null;
            string endSignal = null;
            
            // Set to one inside the loop
            int lineCount = 0;

            int lastLineLength = 1;

            // Set the user input color
            Console.ForegroundColor = CodeColor;

            // Create a starting prefix, this is shown for each line
            string prefix = GetWorkingDir( true ) + ": ";

            // Retreive one command from the user
            // This may be a single line, functions, loop or conditional
            // For single line commands it only does one loop
            // For multi-line commands it loops until an end character is found
            do {
                // Start each line as blank
                textLine = "";

                // Count the lines, this allows us to not show the WorkingDir
                // on lines after the first
                lineCount++;

                // If this is not the first line of the command remove the prefix
                if (lineCount != 1) {
                    prefix = "    "; // four spaces
                }

                // On the new line write the prefix
                Console.Write( prefix );

                // Recieve one line of input
                do {
                    // pressedKey is used to record user input and also
                    // check for special keys, e.g., backspace and enter
                    pressedKey = Console.ReadKey();
                    if (pressedKey.Key != ConsoleKey.Enter) {
                        inputChar = pressedKey.KeyChar;
                        textLine += inputChar;
                    }

                    // If backspace is pressed, delete the last character and 
                    // update the console string
                    if (pressedKey.Key == ConsoleKey.Backspace && textLine.Length > 1) {
                        int startPos = Console.CursorLeft;
                        textLine = textLine.Remove( textLine.Length - 2 );
                        //string prefix = "";


                        // Print the text of the current line
                        // This deletes any text previously on this line
                        Console.Write( "\r{0}{1}", prefix, textLine );

                        // Pad the line with white space
                        int lineLength = textLine.Length; // +prefix.Length;
                        for (int i = lastLineLength - lineLength; i > 0; i--) {
                            Console.Write( " " );
                        }

                        Console.CursorLeft = textLine.Length + prefix.Length;
                    }
                    lastLineLength = textLine.Length;
                } while (pressedKey.Key != ConsoleKey.Enter);

                // Enter doesn't start a new line by default, so we force it here
                Console.WriteLine();
                
                if (endSignal == null || endSignal == "...") {
                    endSignal = GetEndSignal( textLine );
                }

                // Save all user input to a string
                // If the command is only one line, this will only happen once
                // If this is the first (or only) line there is no need for a trailing \n
                // Instead they are put between lines if there are multiple
                if (lineCount != 1) {
                    userInput += "\n";
                }
                userInput += textLine;
            } while (endSignal != null && textLine.StartsWith( endSignal ) == false);

            // Restore the color we set above
            Console.ResetColor();

            return userInput;
        }

        /// <summary>
        /// Get the current working directory and optionally the user name
        /// </summary>
        /// <param name="showUser">If true the return will be prefixed with "user@", where "user" 
        /// is the name of the Windows account which launched this program</param>
        /// <returns>The complete working directory in the form of a string</returns>
        public static string GetWorkingDir(bool showUser) {
            string user = "";
            string output = null;
            if (showUser == true) {
                AhkDll.ahkExec( "__cmdtemp := A_UserName" );
                user = AhkDll.ahkgetvar( "__cmdtemp", false );
                output = user + "@";
            }
            //return AhkDll.ahkgetvar( "myvar", false );
            //AhkDll.ahkExec( "__cmdtemp := A_WorkingDir" );
            return user; // +AhkDll.ahkgetvar( "__cmdtemp", false );
        }

        /// <summary>
        /// Get the current working directory
        /// </summary>
        /// <returns>The complete working directory in the form of a string</returns>
        public static string GetWorkingDir() {
            return AhkDll.ahkgetvar( "A_WorkingDir", false );
        }

        /// <summary>
        /// Analize a code line to dertimine if it's multiline
        /// </summary>
        /// <param name="codeline">One line of AutoHotkey code</param>
        /// <returns>For multiline commands it returns the end signal, "return" or "}".  Otherwise null.</returns>
        public static string GetEndSignal(string codeline) {
            // Make all comparisons lowercase and remove surrounding whitespace
            codeline = codeline.ToLower().Trim();

            // It's only a label if it has : and nothing after it
            // Hotkeys are labels and recieve no special treatment in this case
            bool isLabel = codeline.EndsWith( ":" );

            // All block elements follow the same rules
            // We don't care about the opening curly bracket.  Further more it isn't
            // Reliable
            bool isBlock = codeline.StartsWith( "while" ) || codeline.StartsWith( "if" )
                || codeline.StartsWith( "for" ) || codeline.StartsWith( "loop" );

            bool isOpenBrace = codeline.EndsWith( "{" );

            // Labels must end with a return, hotkeys are the same
            if (isLabel) {
                return "return";
            }

            // Once an opening brace ('{') is found a closing brace ('}') is needed
            // This is listed before isBlock because many blocks can have a { 
            // on the first line.  This isn't fool proof because some conditional 
            // blocks (non-expressions) don't allow a { on the same line.  If
            // this does happen, it's likely user error and illegal in AHK.
            // They will get a syntax error from the intrepreter.
            else if (isOpenBrace) {
                return "}";
            }
            else if (isBlock) {
                return "..."; //
            }
            else
                return null;
        }

        private static void InstallDll() {

            // If AutoHotkey.dll already exists, we don't need to do anything
            if (File.Exists( "AutoHotkey.dll" ) == false) {

                // Write the binary data of AutoHotkey.dll to disk
                File.WriteAllBytes( "AutoHotkey.dll", Properties.Resources.AutoHotkey );
            }
        }
    }
}
