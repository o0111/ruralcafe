using Lucene.Net.Analysis;
using Lucene.Net.Highlight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.Lucenenet
{
    /// <summary>
    /// Implementation that fragments a text into sentences. Taken from
    /// http://mail-archives.apache.org/mod_mbox/lucene-dev/200801.mbox/%3C14690255.post@talk.nabble.com%3E
    /// and adapted from Java to C#.
    /// 
    /// Results are "OK".
    /// </summary>
    class SentenceFragmenter : Fragmenter 
    {
	    private const int DEFAULT_FRAGMENT_SIZE = 300;
	    private int currentNumFrags;
	    private int fragmentSize;
	    private String text;

        public SentenceFragmenter()
            : this(DEFAULT_FRAGMENT_SIZE)
        {
        }
	
	    public SentenceFragmenter(int fragmentSize)
	    {
		    this.fragmentSize=fragmentSize;
	    }


        public virtual void Start(String originalText)
	    {
		   this.text = originalText;
		    currentNumFrags=1;
	    }

	    public bool isCriticalChar (char c) {
		    return (c == '.'|| c == '?' || c == '!');
	    }

        public virtual bool IsNewFragment(Token token)
	    {
            char kar1 = this.text[token.StartOffset() - 2];
            char kar2 = this.text[token.StartOffset() - 3];
            char kar3 = this.text[token.StartOffset() - 4];
		
		
		    bool isNewFrag= ((token.EndOffset()>=(fragmentSize*(currentNumFrags - 1) + (fragmentSize/2))&& 
				    (isCriticalChar(kar1) || isCriticalChar(kar2) || isCriticalChar(kar3)))
				    || (token.EndOffset()>=(fragmentSize*currentNumFrags)));
		    if(isNewFrag)
		    {
			    currentNumFrags++;
		    }
		    return isNewFrag;
	    }

        public virtual int GetFragmentSize()
	    {
		    return fragmentSize;
	    }

	    public virtual void setFragmentSize(int size)
	    {
		    fragmentSize = size;
	    }
    }
}
