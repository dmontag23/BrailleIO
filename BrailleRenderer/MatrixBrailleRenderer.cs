﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BrailleIO.Renderer.BrailleInterpreter;
using BrailleIO.Renderer.Structs;
using BrailleIO.Interface;

namespace BrailleIO.Renderer
{
    /// <summary>
    /// Renders a String object into a dot pattern using a braille renderer. 
    /// </summary>
    /// <seealso cref="BrailleIO.Renderer.AbstractCachingRendererBase" />
    /// <seealso cref="BrailleIO.Interface.IBrailleIOContentRenderer" />
    public partial class MatrixBrailleRenderer : AbstractCachingRendererBase, IBrailleIOContentRenderer
    {
        #region Members

        /// <summary>
        /// Interprets dots as Characters and vise versa
        /// </summary>
        public readonly IBraileInterpreter BrailleInterpreter;

        /// <summary>
        /// Gets or sets the rendering properties.
        /// </summary>
        /// <value>The rendering properties.</value>
        public RenderingProperties RenderingProperties { get; set; }


        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MatrixBrailleRenderer"/> class.
        /// </summary>
        /// <param name="renderingProperties">The rendering properties.</param>
        public MatrixBrailleRenderer(RenderingProperties renderingProperties = RenderingProperties.NONE)
            : this(new SimpleBrailleInterpreter(), renderingProperties){}

        /// <summary>
        /// Initializes a new instance of the <see cref="MatrixBrailleRenderer"/> class.
        /// </summary>
        /// <param name="brailleInterpreter">The braille interpreter. Interprets characters and turn them into dott patterns.</param>
        /// <param name="renderingProperties">The rendering properties to indiviualize the rendering result.</param>
        public MatrixBrailleRenderer(IBraileInterpreter brailleInterpreter, RenderingProperties renderingProperties = RenderingProperties.NONE)
        {
            BrailleInterpreter = brailleInterpreter;
            RenderingProperties = renderingProperties;
        }

        #endregion

        #region IBrailleIOContentRenderer

        /// <summary>
        /// Renders a content object into an boolean matrix;
        /// while <c>true</c> values indicating raised pins and <c>false</c> values indicating lowered pins
        /// </summary>
        /// <param name="view">The frame to render in. This gives access to the space to render and other parameters. Normally this is a BrailleIOViewRange/&gt;.</param>
        /// <param name="content">The content to render.</param>
        /// <param name="callHooks">if set to <c>true</c> [call the pre- and post-rendering hooks].</param>
        /// <returns>
        /// A two dimensional boolean M x N matrix (bool[M,N]) where M is the count of rows (this is height)
        /// and N is the count of columns (which is the width).
        /// Positions in the Matrix are of type [i,j]
        /// while i is the index of the row (is the y position)
        /// and j is the index of the column (is the x position).
        /// In the matrix <c>true</c> values indicating raised pins and <c>false</c> values indicating lowered pins
        /// </returns>
        public override bool[,] RenderMatrix(IViewBoxModel view, object content, bool callHooks = true)
        {
            if(callHooks)callAllPreHooks(ref view, ref content);

            int width = view.ContentBox.Width;
            bool scrolleBars = false;
            var matrix = RenderMatrix(width, content, scrolleBars);

            view.ContentHeight = matrix.GetLength(0);
            view.ContentWidth = matrix.GetLength(1);

            if (callHooks) callAllPostHooks(view, content, ref matrix);

            return matrix;
        }

        /// <summary>
        /// Renders the matrix.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="content">The content.</param>
        /// <param name="scrollbars">if set to <c>true</c> [scrollbars] will be rendered.</param>
        /// <returns></returns>
        public bool[,] RenderMatrix(int width, object content, bool scrollbars = false)
        {
            lock (_rendererLock)
            {
                clearRenderElements();

                if (content != null && content is String)
                {
                    String text = content.ToString();
                    if (BrailleInterpreter != null)
                    {
                        //reduce the with if scrollbars should been shown
                        width -= scrollbars | RenderingProperties.HasFlag(RenderingProperties.ADD_SPACE_FOR_SCROLLBARS) ? 3 : 0;

                        int maxUsedWidth = 0;

                        //check available width
                        if (width > 0)
                        {
                            List<List<List<int>>> lines = new List<List<List<int>>>();

                            // split text into paragraphs/lines
                            string[] paragraphs = GetLinesOfString(text);

                            foreach (var p in paragraphs)
                            {
                                int offset = lines.Count * (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT);
                                var paragraphLines = renderParagraph(p, width, ref maxUsedWidth, offset);
                                lines.AddRange(paragraphLines);
                            }

                            //reduce matrix if requested to minimum width
                            if (this.RenderingProperties.HasFlag(RenderingProperties.RETURN_REAL_WIDTH))
                            {
                                width = maxUsedWidth + (scrollbars ? 3 : 0);
                            }

                            // build start matrix
                            bool[,] matrix = buildMatrixFromLines(lines, width);

                            return matrix;
                        }
                    }
                }
                return null;
            }
        }

        #endregion

        #region String Handling

        /// <summary>
        /// Split the given String into word by searching for 'spacing characters'.
        /// </summary>
        /// <param name="text">The text to split into words.</param>
        /// <returns>An array of separated words without space characters.</returns>
        public static string[] GetWordsOfString(string text)
        {
            return Regex.Split(text, @"\s");
        }

        private static readonly Regex paragraphSplitter = new Regex("\r\n|\r|\n");

        /// <summary>
        /// Gets the lines of string. Which means to split the given String into his paragraphs.
        /// </summary>
        /// <param name="text">The text to split hat his 'line change characters'.</param>
        /// <returns>An array of separated lines/paragraphs.</returns>
        public static string[] GetLinesOfString(string text)
        {
            return paragraphSplitter.Split(text);
        }

        /// <summary>
        /// Gets the Braille interpretation for the string.
        /// </summary>
        /// <param name="text">The text to convert into Braille.</param>
        /// <returns>The Braille dot patterns for the string. 
        /// The result is a list of Braille-character dot patterns. 
        /// Each sublist stands for one Braille cell, containing a list 
        /// of raised pin positions inside a Braille cell. </returns>
        List<List<int>> getBrailleFromString(String text)
        {
            return BrailleInterpreter.GetDotsFromString(text);
        }

        #endregion

        #region Rendering

        /// <summary>
        /// predefined with of one Braille cell (without spacing)
        /// </summary>
        public const int BRAILLE_CHAR_WIDTH = 2;
        /// <summary>
        /// predefined height for one Braille cell (without spacing)
        /// </summary>
        public const int BRAILLE_CHAR_HEIGHT = 4;

        /// <summary>
        /// predefined space between two adjacent Braille cells in one line
        /// </summary>
        public const int INTER_CHAR_WIDTH = 1;
        /// <summary>
        /// predefined spacing between two adjacent lines.
        /// </summary>
        public const int INTER_LINE_HEIGHT = 1;

        #region Lenght Calculations

        /// <summary>
        /// Tries to estimates the need for scroll bar.
        /// </summary>
        /// <param name="content">The content to render.</param>
        /// <param name="width">The available width for the result.</param>
        /// <param name="height">The available height for the result.</param>
        /// <returns><c>true</c> if the given space is not enough and vertical scrollbars are needed, otherwise <c>false</c>.</returns>
        public static bool EstimateNeedOfScrollBar(string content, int width, int height)
        {
            if (!String.IsNullOrEmpty(content) && width > 0 && height > 0)
            {
                int maxCharsToFit = getMaxCountOfChars(width) * getMaxCountOfLines(height);
                if (maxCharsToFit < (content.Length * 0.8)) return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the minimal width consumed by an rendered string including inter character
        /// space without inter character space at the end of the String.
        /// </summary>
        /// <param name="brailleChars">The braille chars.</param>
        /// <returns>the minimum width consumed by the string</returns>
        static int getMinWidthOfString(List<List<int>> brailleChars)
        {
            return getMaxWidthOfString(brailleChars) - INTER_CHAR_WIDTH;
        }

        /// <summary>
        /// Gets the max width consumed by an rendered string including inter character
        /// space and inter character space at the end of the String.
        /// </summary>
        /// <param name="brailleChars">The braille chars.</param>
        /// <returns>the maximum width consumed by the string including separating space at the end of the string</returns>
        static int getMaxWidthOfString(List<List<int>> brailleChars)
        {
            return (brailleChars.Count * BRAILLE_CHAR_WIDTH) + (brailleChars.Count * INTER_CHAR_WIDTH);
        }

        /// <summary>
        /// Gets the max count of chars that would fit into the given width.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <returns>the number of chars that would fit</returns>
        static int getMaxCountOfChars(int width)
        {
            double ratio = (double)(width + INTER_CHAR_WIDTH) / (double)(BRAILLE_CHAR_WIDTH + INTER_CHAR_WIDTH);
            return (int)Math.Floor(ratio);
        }

        /// <summary>
        /// Gets the max count of chars that would fit into the given width.
        /// </summary>
        /// <param name="height">The height.</param>
        /// <returns>
        /// the number of chars that would fit
        /// </returns>
        static int getMaxCountOfLines(int height)
        {
            double ratio = (double)(height + INTER_LINE_HEIGHT) / (double)(BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT);
            return (int)Math.Floor(ratio);
        }

        /// <summary>
        /// Checks if a word fits into an available width.
        /// </summary>
        /// <param name="brailleChars">The braille chars.</param>
        /// <param name="availableWidth">Available width.</param>
        /// <returns><c>true</c> if the word fits with his minimal lenght into the available space. Otherwise <c>false</c>.</returns>
        bool fitsWordInRestOfLine(List<List<int>> brailleChars, int availableWidth)
        {
            if (getMinWidthOfString(brailleChars) <= availableWidth) return true;
            return false;
        }

        #endregion

        #region Text to Braille Dot-Pattern Lines

        /// <summary>
        /// Renders a paragraph.
        /// </summary>
        /// <param name="text">The text to render.</param>
        /// <param name="width">The available width.</param>
        /// <param name="maxUsedWidth">Maximum width of the used.</param>
        /// <param name="yOffset">The y offset.</param>
        /// <returns></returns>
        private List<List<List<int>>> renderParagraph(string text, int width, ref int maxUsedWidth, int yOffset = 0)
        {
            List<List<List<int>>> lines = new List<List<List<int>>>();
            if (width > 0)
            {              
                string[] words = GetWordsOfString(text);

                List<List<int>> currentLine = new List<List<int>>();
                int availableWidth = width;


                int peX, peY, peW, peH = 0;
                peX = peY = peW = peH;
                peY = yOffset + lines.Count * (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT);
                peX = width - availableWidth;
                RenderElement pe = new RenderElement(peX, peY, peW, peH, text);
                pe.Type = BrailleRendererPartType.PARAGRAPH;


                foreach (var word in words)
                {
                    #region Rendering Element

                    int eX, eY, eW, eH = 0;
                    eX = eY = eW = eH;
                    eY = yOffset + lines.Count * (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT);
                    eX = width - availableWidth;
                    RenderElement e = new RenderElement(eX, eY, eW, eH, word);
                    e.Type = BrailleRendererPartType.WORD;

                    #endregion

                    // get the dot patterns for that word
                    List<List<int>> dots = getBrailleFromString(word);

                    // check if the line has still some entries
                    // if so, add a space char or open a new line
                    if (currentLine.Count > 0)
                    {
                        // check if the line is full
                        if (getMaxWidthOfString(currentLine) >= width)
                        {
                            makeNewLine(ref lines, ref currentLine, ref availableWidth, width, ref maxUsedWidth);
                        }
                        else
                        {
                            // add a space
                            currentLine.Add(new List<int>());
                            availableWidth -= INTER_CHAR_WIDTH + BRAILLE_CHAR_WIDTH;
                        }
                    }

                    //check if it fits into the available space
                    if (!fitsWordInRestOfLine(dots, availableWidth)) //no
                    {
                        makeNewLine(ref lines, ref currentLine, ref availableWidth, width, ref maxUsedWidth);
                    }

                    e.X = width - availableWidth;
                    e.Y = yOffset + lines.Count * (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT);

                    int minWidth = getMinWidthOfString(dots);
                    if (minWidth > width)
                    {
                        //this will start a new line, so add a new line to the y position
                        e.Y += BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT;

                        //split the word into several lines
                        List<List<List<int>>> wordLines = splitWordOverLines(dots, width, ref e);

                        if (wordLines != null && wordLines.Count > 0)
                        {
                            //remove empty line if generated twice
                            if (lines.Count > 0 && lines[lines.Count - 1].Count == 0)
                            {
                                lines.Remove(lines[Math.Max(0, lines.Count - 1)]);
                            }

                            // add them to the lines
                            lines.AddRange(wordLines);
                            //set the current line
                            currentLine = lines[lines.Count - 1];

                            //remove from list so it will not added twice
                            lines.Remove(currentLine);

                            //update the available size
                            availableWidth = width - getMinWidthOfString(currentLine);
                        }
                    }
                    else
                    {
                        //fill the word into the line
                        currentLine.AddRange(dots);

                        e.Width = minWidth + INTER_CHAR_WIDTH;
                        e.Height = BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT;

                        //update the available width
                        availableWidth -= getMinWidthOfString(dots) + INTER_CHAR_WIDTH;
                    }

                    #region Rendering Element

                    pe.AddSubPart(e);

                    #endregion
                }
                lines.Add(currentLine);

                // update the maxUsed Width
                maxUsedWidth = Math.Max(maxUsedWidth, (width - availableWidth));
                elements.AddLast(pe);
            }

            return lines;
        }

        /// <summary>
        /// Splits one word over several lines if it is to long to fit in one line.
        /// </summary>
        /// <param name="dots">The dot patterns of the word.
        /// (List of characters) List [
        /// (List of raised pins in one Braille cell) List [dot pattern]
        /// ]</param>
        /// <param name="width">The width.</param>
        /// <param name="parentElement">The parent element.</param>
        /// <returns>
        /// A list of lines for the split word.
        /// (List of lines) List [
        /// (List of characters) List [
        /// (List of raised pins in one Braille cell) List [dot pattern]
        /// ]
        /// ]
        /// </returns>
        private static List<List<List<int>>> splitWordOverLines(List<List<int>> dots, int width, ref RenderElement parentElement)
        {
            List<List<List<int>>> lines = new List<List<List<int>>>();
            if (dots != null && width > BRAILLE_CHAR_WIDTH)
            {
                //Split the word char list in peaces width max length of 'width'
                int count = getMaxCountOfChars(width);
                if (count > 0)
                {
                    string parVal = parentElement.GetValue() != null ? parentElement.GetValue().ToString() : String.Empty;
                    int i = 0;

                    while (i < dots.Count)
                    {
                        int l = (i + count) < dots.Count ? count : (dots.Count - i);
                        List<List<int>> subList = dots.GetRange(i, l);

                        // create sub value
                        string val = !String.IsNullOrEmpty(parVal) && parVal.Length > i ?
                            parVal.Substring(i,
                                (parVal.Length > (i + l) ? l : parVal.Length - i)
                            ) : String.Empty;

                        // create sub element for each part
                        RenderElement se = new RenderElement(
                            parentElement.X,
                            parentElement.Y + (lines.Count * (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT)),
                            getMinWidthOfString(subList),
                            (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT),
                            val,
                            parentElement
                            );
                        parentElement.AddSubPart(se);
                        se.Type = BrailleRendererPartType.WORD_PART;

                        lines.Add(subList);
                        i += count;
                    }
                }
            }
            return lines;
        }

        /// <summary>
        /// Saves the current line to the lines list and opens a new line
        /// </summary>
        /// <param name="lines">The list of filled lines.
        /// (List of lines) List [
        ///     (List of characters) List [
        ///         (List of raised pins in one Braille cell) List [dot pattern]
        ///     ]
        /// ]</param>
        /// <param name="currentLine">The current line to fill with chars.
        /// (List of characters) List [
        /// (List of raised pins in one Braille cell) List [dot pattern]
        /// ]</param>
        /// <param name="availableWidth">Current available space for chars on the current line.</param>
        /// <param name="width">The max width of a line.</param>
        /// <param name="maxUsedWidth">maximum occupied width.</param>
        private static void makeNewLine(ref List<List<List<int>>> lines, ref List<List<int>> currentLine, ref int availableWidth, int width, ref int maxUsedWidth)
        {
            //save the line and open a new one
            lines.Add(currentLine);
            currentLine = new List<List<int>>();

            maxUsedWidth = Math.Max(maxUsedWidth, (width - availableWidth));

            //reset available with
            availableWidth = width;
        }

        #endregion

        #region Build Bool/Dot Matrix

        /// <summary>
        /// Converts the interpreted Lines into a bool matrix.
        /// </summary>
        /// <param name="lines">The lines containing dot patterns.
        /// (List of lines) List [
        ///     (List of characters) List [
        ///         (List of raised pins in one Braille cell) List [dot pattern]
        ///     ]
        /// ]
        /// </param>
        /// <param name="width">The width of the matrix to build out of the lines.</param>
        /// <returns>A two dimensional matrix of the given width and the needed height.</returns>
        private bool[,] buildMatrixFromLines(List<List<List<int>>> lines, int width)
        {
            int height = 0;
            if (lines != null)
            {
                height = lines.Count * (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT);
                if (this.RenderingProperties.HasFlag(RenderingProperties.IGNORE_LAST_LINESPACE)) { height -= INTER_LINE_HEIGHT; }
            }

            bool[,] m = new bool[height, width];

            if (lines != null)
            {
                //do each line
                //for (int lineNumber = 0; lineNumber < lines.Count; lineNumber++) // do this as a parallel for?!
                Parallel.For(0, lines.Count, (lineNumber) =>
                {
                    List<List<int>> line = lines[lineNumber];
                    int yoffset = lineNumber * (BRAILLE_CHAR_HEIGHT + INTER_LINE_HEIGHT);

                    // do every char
                    //for (int charNumber = 0; charNumber < line.Count; charNumber++)
                    Parallel.For(0, line.Count, (charNumber) =>
                    {
                        List<int> brailleChar = line[charNumber];
                        int xOffset = charNumber * (BRAILLE_CHAR_WIDTH + INTER_CHAR_WIDTH);

                        addDotPatternToMatrix(ref m, brailleChar, xOffset, yoffset);
                    }
                    );
                }
                );
            }

            return m;
        }

        /// <summary>
        /// Adds the dots of a char dot pattern to a matrix.
        /// </summary>
        /// <param name="m">The matrix to fill.</param>
        /// <param name="brailleChar">The braille char to add. 
        /// (List of raised pins in one Braille cell) List [dot pattern]</param>
        /// <param name="xOffset">The x offset for the chr to place.</param>
        /// <param name="yoffset">The yoffset for the char to place.</param>
        private static void addDotPatternToMatrix(ref bool[,] m, List<int> brailleChar, int xOffset, int yoffset)
        {
            if (brailleChar != null
                && m != null && m.GetLength(0) > 0 && m.GetLength(1) > 0)
            {
                for (int dotNumber = 0; dotNumber < brailleChar.Count; dotNumber++)
                {
                    int x, y = -1;
                    bool success = tryGetPinPositionOfbRailleDot(brailleChar[dotNumber], out x, out y);
                    if (success && x >= 0 && y >= 0)
                    {
                        x += xOffset;
                        y += yoffset;

                        if (y >= 0 && y < m.GetLength(0)
                            && x >= 0 && x < m.GetLength(1))
                        {
                            m[y, x] = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Tries the get pin position of a Braille dot e.g. the dot 5 has the position x=1 y=1
        /// </summary>
        /// <param name="dot">The dot.</param>
        /// <param name="x">The returning x position of this dot.</param>
        /// <param name="y">The returning y position of this dot.</param>
        /// <returns><c>true</c> if it was posible to mathch the dot to a position. Otherwise <c>false</c></returns>
        static bool tryGetPinPositionOfbRailleDot(int dot, out int x, out int y)
        {
            if (dot < 9 && dot > 0)
            {
                x = y = -1;
                switch (dot)
                {
                    case 1:
                        x = 0; y = 0;
                        break;
                    case 2:
                        x = 0; y = 1;
                        break;
                    case 3:
                        x = 0; y = 2;
                        break;
                    case 4:
                        x = 1; y = 0;
                        break;
                    case 5:
                        x = 1; y = 1;
                        break;
                    case 6:
                        x = 1; y = 2;
                        break;
                    case 7:
                        x = 0; y = 3;
                        break;
                    case 8:
                        x = 1; y = 3;
                        break;
                    default:
                        return false;
                }
                return true;
            }
            x = y = -1;
            return false;
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// Specifies how the renderer handles some special objects and properties
    /// </summary>
    [Flags]
    public enum RenderingProperties
    {
        /// <summary>
        /// No special rendering 
        /// </summary>
        NONE = 0,
        /// <summary>
        /// The last line space should be ignored. Normally after each line 
        /// a spacing line is rendered. To remove this spacing line from 
        /// the last line activate this flag.
        /// </summary>
        IGNORE_LAST_LINESPACE = 1,
        /// <summary>
        /// Return the matrix with the real used width instead of the given width.
        /// will maybe reduce the returned matrix in number of columns.
        /// </summary>
        RETURN_REAL_WIDTH = 2,
        /// <summary>
        /// Adds some free space on the right side of the returned matrix to place scrollbars.
        /// Should not combined with <see cref="RenderingProperties.RETURN_REAL_WIDTH"/>.
        /// </summary>
        ADD_SPACE_FOR_SCROLLBARS = 4,
    }

}