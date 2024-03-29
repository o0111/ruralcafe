#region Copyright (c) 2004, Ryan Whitaker
/*********************************************************************************
'
' Copyright (c) 2004 Ryan Whitaker
'
' This software is provided 'as-is', without any express or implied warranty. In no 
' event will the authors be held liable for any damages arising from the use of this 
' software.
' 
' Permission is granted to anyone to use this software for any purpose, including 
' commercial applications, and to alter it and redistribute it freely, subject to the 
' following restrictions:
'
' 1. The origin of this software must not be misrepresented; you must not claim that 
' you wrote the original software. If you use this software in a product, an 
' acknowledgment (see the following) in the product documentation is required.
'
' This product uses software written by the developers of NClassifier
' (http://nclassifier.sourceforge.net).  NClassifier is a .NET port of the Nick
' Lothian's Java text classification engine, Classifier4J 
' (http://classifier4j.sourceforge.net).
'
' 2. Altered source versions must be plainly marked as such, and must not be 
' misrepresented as being the original software.
'
' 3. This notice may not be removed or altered from any source distribution.
'
'********************************************************************************/
#endregion

using System;
using System.Collections;
using System.IO;

namespace NClassifier
{
	public class CustomizableStopWordProvider : IStopWordProvider
	{
		string _path;
		string[] _words;

		public static string DEFAULT_STOPWORD_PROVIDER_FILENAME = "DefaultStopWords.txt";

		/// <param name="filename">
		/// The name of the text file in the app's root that contains a list of stop words, one on each line
		/// </param>
		public CustomizableStopWordProvider(string filename)
		{
			_path = Directory.GetCurrentDirectory() + "\\" + filename;
			Init();
		}

		public CustomizableStopWordProvider() : this(DEFAULT_STOPWORD_PROVIDER_FILENAME) {}

		protected void Init()
		{
			ArrayList wordsList = new ArrayList();
			TextReader reader = File.OpenText(_path);

			string word;
			while ((word = reader.ReadLine()) != null)
				wordsList.Add(word.Trim());

			reader.Close();

			_words = (string[])wordsList.ToArray(typeof(string));

			Array.Sort(_words);
		}

		public bool IsStopWord(string word)
		{
			return (Array.BinarySearch(_words, word) >= 0);
		}
	}
}