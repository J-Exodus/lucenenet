using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Attributes;
using Lucene.Net.Codecs;
using Lucene.Net.Documents;
using Lucene.Net.Randomized.Generators;
using Lucene.Net.Support;
using Lucene.Net.Support.Threading;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Lucene.Net.Index
{
    using Attribute = Lucene.Net.Util.Attribute;
    using BytesRef = Lucene.Net.Util.BytesRef;
    using CharTermAttribute = Lucene.Net.Analysis.TokenAttributes.CharTermAttribute;
    using Codec = Lucene.Net.Codecs.Codec;
    using Directory = Lucene.Net.Store.Directory;
    using Document = Documents.Document;
    using Field = Field;
    using FieldType = FieldType;
    using FixedBitSet = Lucene.Net.Util.FixedBitSet;
    using IAttribute = Lucene.Net.Util.IAttribute;
    using IndexSearcher = Lucene.Net.Search.IndexSearcher;
    using OffsetAttribute = Lucene.Net.Analysis.TokenAttributes.OffsetAttribute;
    using PayloadAttribute = Lucene.Net.Analysis.TokenAttributes.PayloadAttribute;
    using PositionIncrementAttribute = Lucene.Net.Analysis.TokenAttributes.PositionIncrementAttribute;
    using SeekStatus = Lucene.Net.Index.TermsEnum.SeekStatus;
    using StringField = StringField;
    using TermQuery = Lucene.Net.Search.TermQuery;
    using TestUtil = Lucene.Net.Util.TestUtil;
    using TextField = TextField;

    /*
    * Licensed to the Apache Software Foundation (ASF) under one or more
    * contributor license agreements.  See the NOTICE file distributed with
    * this work for additional information regarding copyright ownership.
    * The ASF licenses this file to You under the Apache License, Version 2.0
    * (the "License"); you may not use this file except in compliance with
    * the License.  You may obtain a copy of the License at
    *
    *     http://www.apache.org/licenses/LICENSE-2.0
    *
    * Unless required by applicable law or agreed to in writing, software
    * distributed under the License is distributed on an "AS IS" BASIS,
    * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    * See the License for the specific language governing permissions and
    * limitations under the License.
    */

    using TokenStream = Lucene.Net.Analysis.TokenStream;

    /// <summary>
    /// Base class aiming at testing <see cref="TermVectorsFormat"/>.
    /// To test a new format, all you need is to register a new <see cref="Codec"/> which
    /// uses it and extend this class and override <see cref="GetCodec()"/>.
    /// <para/>
    /// @lucene.experimental
    /// </summary>
    public abstract class BaseTermVectorsFormatTestCase : BaseIndexFileFormatTestCase
    {
        /// <summary>
        /// A combination of term vectors options.
        /// </summary>
        protected internal enum Options
        {
            NONE,//(false, false, false),
            POSITIONS,//(true, false, false),
            OFFSETS,//(false, true, false),
            POSITIONS_AND_OFFSETS,//(true, true, false),
            POSITIONS_AND_PAYLOADS,//(true, false, true),
            POSITIONS_AND_OFFSETS_AND_PAYLOADS//(true, true, true);
            //		final boolean positions, offsets, payloads;
            //		private Options(boolean positions, boolean offsets, boolean payloads)
            //	{
            //	  this.positions = positions;
            //	  this.offsets = offsets;
            //	  this.payloads = payloads;
            //	}
        }

        private class OptionsWrapper
        {
            internal bool positions, offsets, payloads;

            private void SetOptionsWrapper(bool positions, bool offsets, bool payloads)
            {
                this.positions = positions;
                this.offsets = offsets;
                this.payloads = payloads;
            }

            public OptionsWrapper(Options opt)
            {
                switch (opt)
                {
                    case Options.NONE:
                        SetOptionsWrapper(false, false, false);
                        break;

                    case Options.POSITIONS:
                        SetOptionsWrapper(true, false, false);
                        break;

                    case Options.OFFSETS:
                        SetOptionsWrapper(false, true, false);
                        break;

                    case Options.POSITIONS_AND_OFFSETS:
                        SetOptionsWrapper(true, true, false);
                        break;

                    case Options.POSITIONS_AND_PAYLOADS:
                        SetOptionsWrapper(true, false, true);
                        break;

                    case Options.POSITIONS_AND_OFFSETS_AND_PAYLOADS:
                        SetOptionsWrapper(true, true, true);
                        break;

                    default:
                        throw new InvalidOperationException("Invalid Options enum type");
                }
            }

            public static IEnumerable<Options> GetAsEnumer()
            {
                return Enum.GetValues(typeof(Options)).Cast<Options>();
            }

            public static IEnumerable<Options> GetAsEnumer(Options startInc, Options endInc)
            {
                foreach (Options opt in Enum.GetValues(typeof(Options)))
                {
                    if (opt >= startInc && opt <= endInc)
                        yield return opt;
                }
            }
        }

        protected virtual IEnumerable<Options> ValidOptions()
        {
            return OptionsWrapper.GetAsEnumer();
        }

        protected virtual IEnumerable<Options> ValidOptions(Options startInc, Options endInc)
        {
            return OptionsWrapper.GetAsEnumer(startInc, endInc);
        }

        protected internal virtual Options RandomOptions()
        {
            return RandomPicks.RandomFrom(Random, new List<Options>(ValidOptions()));
        }

        protected internal virtual FieldType FieldType(Options options)
        {
            var ft = new FieldType(TextField.TYPE_NOT_STORED)
            {
                StoreTermVectors = true,
                StoreTermVectorPositions = (new OptionsWrapper(options)).positions,
                StoreTermVectorOffsets = (new OptionsWrapper(options)).offsets,
                StoreTermVectorPayloads = (new OptionsWrapper(options)).payloads
            };
            ft.Freeze();
            return ft;
        }

        protected internal virtual BytesRef RandomPayload()
        {
            int len = Random.Next(5);
            if (len == 0)
            {
                return null;
            }
            BytesRef payload = new BytesRef(len);
            Random.NextBytes(payload.Bytes);
            payload.Length = len;
            return payload;
        }

        protected internal override void AddRandomFields(Document doc)
        {
            foreach (Options opts in ValidOptions())
            {
                FieldType ft = FieldType(opts);
                int numFields = Random.Next(5);
                for (int j = 0; j < numFields; ++j)
                {
                    doc.Add(new Field("f_" + opts, TestUtil.RandomSimpleString(Random, 2), ft));
                }
            }
        }

        // custom impl to test cases that are forbidden by the default OffsetAttribute impl
        private class PermissiveOffsetAttributeImpl : Attribute, IOffsetAttribute
        {
            internal int start, end;

            public int StartOffset
            {
                get { return start; }
            }

            public int EndOffset
            {
                get { return end; }
            }

            public void SetOffset(int startOffset, int endOffset)
            {
                // no check!
                start = startOffset;
                end = endOffset;
            }

            public override void Clear()
            {
                start = end = 0;
            }

            public override bool Equals(object other)
            {
                if (other == this)
                {
                    return true;
                }

                if (other is PermissiveOffsetAttributeImpl)
                {
                    PermissiveOffsetAttributeImpl o = (PermissiveOffsetAttributeImpl)other;
                    return o.start == start && o.end == end;
                }

                return false;
            }

            public override int GetHashCode()
            {
                return start + 31 * end;
            }

            public override void CopyTo(IAttribute target)
            {
                OffsetAttribute t = (OffsetAttribute)target;
                t.SetOffset(start, end);
            }
        }

        // TODO: use CannedTokenStream?
        protected internal class RandomTokenStream : TokenStream
        {
            private readonly BaseTermVectorsFormatTestCase outerInstance;

            internal readonly string[] terms;
            internal readonly BytesRef[] termBytes;
            internal readonly int[] positionsIncrements;
            internal readonly int[] positions;
            internal readonly int[] startOffsets, endOffsets;
            internal readonly BytesRef[] payloads;

            internal readonly IDictionary<string, int?> freqs;
            internal readonly IDictionary<int?, ISet<int?>> positionToTerms;
            internal readonly IDictionary<int?, ISet<int?>> startOffsetToTerms;

            internal readonly ICharTermAttribute termAtt;
            internal readonly IPositionIncrementAttribute piAtt;
            internal readonly IOffsetAttribute oAtt;
            internal readonly IPayloadAttribute pAtt;
            internal int i = 0;

            protected internal RandomTokenStream(BaseTermVectorsFormatTestCase outerInstance, int len, string[] sampleTerms, BytesRef[] sampleTermBytes)
                : this(outerInstance, len, sampleTerms, sampleTermBytes, Rarely())
            {
                this.outerInstance = outerInstance;
            }

            protected internal RandomTokenStream(BaseTermVectorsFormatTestCase outerInstance, int len, string[] sampleTerms, BytesRef[] sampleTermBytes, bool offsetsGoBackwards)
            {
                this.outerInstance = outerInstance;
                terms = new string[len];
                termBytes = new BytesRef[len];
                positionsIncrements = new int[len];
                positions = new int[len];
                startOffsets = new int[len];
                endOffsets = new int[len];
                payloads = new BytesRef[len];
                for (int i = 0; i < len; ++i)
                {
                    int o = Random.Next(sampleTerms.Length);
                    terms[i] = sampleTerms[o];
                    termBytes[i] = sampleTermBytes[o];
                    positionsIncrements[i] = TestUtil.NextInt32(Random, i == 0 ? 1 : 0, 10);
                    if (offsetsGoBackwards)
                    {
                        startOffsets[i] = Random.Next();
                        endOffsets[i] = Random.Next();
                    }
                    else
                    {
                        if (i == 0)
                        {
                            startOffsets[i] = TestUtil.NextInt32(Random, 0, 1 << 16);
                        }
                        else
                        {
                            startOffsets[i] = startOffsets[i - 1] + TestUtil.NextInt32(Random, 0, Rarely() ? 1 << 16 : 20);
                        }
                        endOffsets[i] = startOffsets[i] + TestUtil.NextInt32(Random, 0, Rarely() ? 1 << 10 : 20);
                    }
                }

                for (int i = 0; i < len; ++i)
                {
                    if (i == 0)
                    {
                        positions[i] = positionsIncrements[i] - 1;
                    }
                    else
                    {
                        positions[i] = positions[i - 1] + positionsIncrements[i];
                    }
                }
                if (Rarely())
                {
                    Arrays.Fill(payloads, outerInstance.RandomPayload());
                }
                else
                {
                    for (int i = 0; i < len; ++i)
                    {
                        payloads[i] = outerInstance.RandomPayload();
                    }
                }

                positionToTerms = new Dictionary<int?, ISet<int?>>(len);
                startOffsetToTerms = new Dictionary<int?, ISet<int?>>(len);
                for (int i = 0; i < len; ++i)
                {
                    if (!positionToTerms.ContainsKey(positions[i]))
                    {
                        positionToTerms[positions[i]] = new HashSet<int?>();//size1
                    }
                    positionToTerms[positions[i]].Add(i);
                    if (!startOffsetToTerms.ContainsKey(startOffsets[i]))
                    {
                        startOffsetToTerms[startOffsets[i]] = new HashSet<int?>();//size1
                    }
                    startOffsetToTerms[startOffsets[i]].Add(i);
                }

                freqs = new Dictionary<string, int?>();
                foreach (string term in terms)
                {
                    if (freqs.ContainsKey(term))
                    {
                        freqs[term] = freqs[term] + 1;
                    }
                    else
                    {
                        freqs[term] = 1;
                    }
                }

                AddAttributeImpl(new PermissiveOffsetAttributeImpl());

                termAtt = AddAttribute<ICharTermAttribute>();
                piAtt = AddAttribute<IPositionIncrementAttribute>();
                oAtt = AddAttribute<IOffsetAttribute>();
                pAtt = AddAttribute<IPayloadAttribute>();
            }

            public virtual bool HasPayloads()
            {
                foreach (BytesRef payload in payloads)
                {
                    if (payload != null && payload.Length > 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            public sealed override bool IncrementToken()
            {
                if (i < terms.Length)
                {
                    termAtt.SetLength(0).Append(terms[i]);
                    piAtt.PositionIncrement = positionsIncrements[i];
                    oAtt.SetOffset(startOffsets[i], endOffsets[i]);
                    pAtt.Payload = payloads[i];
                    ++i;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        protected internal class RandomDocument
        {
            private readonly BaseTermVectorsFormatTestCase outerInstance;

            internal readonly string[] fieldNames;
            internal readonly FieldType[] fieldTypes;
            internal readonly RandomTokenStream[] tokenStreams;

            protected internal RandomDocument(BaseTermVectorsFormatTestCase outerInstance, int fieldCount, int maxTermCount, Options options, string[] fieldNames, string[] sampleTerms, BytesRef[] sampleTermBytes)
            {
                this.outerInstance = outerInstance;
                if (fieldCount > fieldNames.Length)
                {
                    throw new System.ArgumentException();
                }
                this.fieldNames = new string[fieldCount];
                fieldTypes = new FieldType[fieldCount];
                tokenStreams = new RandomTokenStream[fieldCount];
                Arrays.Fill(fieldTypes, outerInstance.FieldType(options));
                HashSet<string> usedFileNames = new HashSet<string>();
                for (int i = 0; i < fieldCount; ++i)
                {
                    // LUCENENET NOTE: Using a simple Linq query to filter rather than using brute force makes this a lot
                    // faster (and won't infinitely retry due to poor random distribution).
                    this.fieldNames[i] = RandomPicks.RandomFrom(Random, fieldNames.Except(usedFileNames).ToArray());
                    //do
                    //{
                    //    this.FieldNames[i] = RandomInts.RandomFrom(Random(), fieldNames);
                    //} while (usedFileNames.Contains(this.FieldNames[i]));

                    usedFileNames.Add(this.fieldNames[i]);
                    tokenStreams[i] = new RandomTokenStream(outerInstance, TestUtil.NextInt32(Random, 1, maxTermCount), sampleTerms, sampleTermBytes);
                }
            }

            public virtual Document ToDocument()
            {
                Document doc = new Document();
                for (int i = 0; i < fieldNames.Length; ++i)
                {
                    doc.Add(new Field(fieldNames[i], tokenStreams[i], fieldTypes[i]));
                }
                return doc;
            }
        }

        protected internal class RandomDocumentFactory
        {
            private readonly BaseTermVectorsFormatTestCase outerInstance;

            internal readonly string[] fieldNames;
            internal readonly string[] terms;
            internal readonly BytesRef[] termBytes;

            protected internal RandomDocumentFactory(BaseTermVectorsFormatTestCase outerInstance, int distinctFieldNames, int disctinctTerms)
            {
                this.outerInstance = outerInstance;
                HashSet<string> fieldNames = new HashSet<string>();
                while (fieldNames.Count < distinctFieldNames)
                {
                    fieldNames.Add(TestUtil.RandomSimpleString(Random));
                    fieldNames.Remove("id");
                }
                this.fieldNames = fieldNames.ToArray(/*new string[0]*/);
                terms = new string[disctinctTerms];
                termBytes = new BytesRef[disctinctTerms];
                for (int i = 0; i < disctinctTerms; ++i)
                {
                    terms[i] = TestUtil.RandomRealisticUnicodeString(Random);
                    termBytes[i] = new BytesRef(terms[i]);
                }
            }

            public virtual RandomDocument NewDocument(int fieldCount, int maxTermCount, Options options)
            {
                return new RandomDocument(outerInstance, fieldCount, maxTermCount, options, fieldNames, terms, termBytes);
            }
        }

        protected internal virtual void AssertEquals(RandomDocument doc, Fields fields)
        {
            // compare field names
            Assert.AreEqual(doc == null, fields == null);
            Assert.AreEqual(doc.fieldNames.Length, fields.Count);
            HashSet<string> fields1 = new HashSet<string>();
            HashSet<string> fields2 = new HashSet<string>();
            for (int i = 0; i < doc.fieldNames.Length; ++i)
            {
                fields1.Add(doc.fieldNames[i]);
            }
            foreach (string field in fields)
            {
                fields2.Add(field);
            }
            Assert.IsTrue(fields1.SetEquals(fields2));

            for (int i = 0; i < doc.fieldNames.Length; ++i)
            {
                AssertEquals(doc.tokenStreams[i], doc.fieldTypes[i], fields.GetTerms(doc.fieldNames[i]));
            }
        }

        new protected internal static bool Equals(object o1, object o2)
        {
            if (o1 == null)
            {
                return o2 == null;
            }
            else
            {
                return o1.Equals(o2);
            }
        }

        // to test reuse
        private readonly ThreadLocal<TermsEnum> termsEnum = new ThreadLocal<TermsEnum>();

        private readonly ThreadLocal<DocsEnum> docsEnum = new ThreadLocal<DocsEnum>();
        private readonly ThreadLocal<DocsAndPositionsEnum> docsAndPositionsEnum = new ThreadLocal<DocsAndPositionsEnum>();

        protected internal virtual void AssertEquals(RandomTokenStream tk, FieldType ft, Terms terms)
        {
            Assert.AreEqual(1, terms.DocCount);
            int termCount = (new HashSet<string>(Arrays.AsList(tk.terms))).Count;
            Assert.AreEqual(termCount, terms.Count);
            Assert.AreEqual(termCount, terms.SumDocFreq);
            Assert.AreEqual(ft.StoreTermVectorPositions, terms.HasPositions);
            Assert.AreEqual(ft.StoreTermVectorOffsets, terms.HasOffsets);
            Assert.AreEqual(ft.StoreTermVectorPayloads && tk.HasPayloads(), terms.HasPayloads);
            HashSet<BytesRef> uniqueTerms = new HashSet<BytesRef>();
            foreach (string term in tk.freqs.Keys)
            {
                uniqueTerms.Add(new BytesRef(term));
            }
            BytesRef[] sortedTerms = uniqueTerms.ToArray(/*new BytesRef[0]*/);
            Array.Sort(sortedTerms, terms.Comparer);
            TermsEnum termsEnum = terms.GetIterator(Random.NextBoolean() ? null : this.termsEnum.Value);
            this.termsEnum.Value = termsEnum;
            for (int i = 0; i < sortedTerms.Length; ++i)
            {
                BytesRef nextTerm = termsEnum.Next();
                Assert.AreEqual(sortedTerms[i], nextTerm);
                Assert.AreEqual(sortedTerms[i], termsEnum.Term);
                Assert.AreEqual(1, termsEnum.DocFreq);

                FixedBitSet bits = new FixedBitSet(1);
                DocsEnum docsEnum = termsEnum.Docs(bits, Random.NextBoolean() ? null : this.docsEnum.Value);
                Assert.AreEqual(DocsEnum.NO_MORE_DOCS, docsEnum.NextDoc());
                bits.Set(0);

                docsEnum = termsEnum.Docs(Random.NextBoolean() ? bits : null, Random.NextBoolean() ? null : docsEnum);
                Assert.IsNotNull(docsEnum);
                Assert.AreEqual(0, docsEnum.NextDoc());
                Assert.AreEqual(0, docsEnum.DocID);
                Assert.AreEqual(tk.freqs[termsEnum.Term.Utf8ToString()], (int?)docsEnum.Freq);
                Assert.AreEqual(DocsEnum.NO_MORE_DOCS, docsEnum.NextDoc());
                this.docsEnum.Value = docsEnum;

                bits.Clear(0);
                DocsAndPositionsEnum docsAndPositionsEnum = termsEnum.DocsAndPositions(bits, Random.NextBoolean() ? null : this.docsAndPositionsEnum.Value);
                Assert.AreEqual(ft.StoreTermVectorOffsets || ft.StoreTermVectorPositions, docsAndPositionsEnum != null);
                if (docsAndPositionsEnum != null)
                {
                    Assert.AreEqual(DocsEnum.NO_MORE_DOCS, docsAndPositionsEnum.NextDoc());
                }
                bits.Set(0);

                docsAndPositionsEnum = termsEnum.DocsAndPositions(Random.NextBoolean() ? bits : null, Random.NextBoolean() ? null : docsAndPositionsEnum);
                Assert.AreEqual(ft.StoreTermVectorOffsets || ft.StoreTermVectorPositions, docsAndPositionsEnum != null);
                if (terms.HasPositions || terms.HasOffsets)
                {
                    Assert.AreEqual(0, docsAndPositionsEnum.NextDoc());
                    int freq = docsAndPositionsEnum.Freq;
                    Assert.AreEqual(tk.freqs[termsEnum.Term.Utf8ToString()], (int?)freq);
                    if (docsAndPositionsEnum != null)
                    {
                        for (int k = 0; k < freq; ++k)
                        {
                            int position = docsAndPositionsEnum.NextPosition();
                            ISet<int?> indexes;
                            if (terms.HasPositions)
                            {
                                indexes = tk.positionToTerms[position];
                                Assert.IsNotNull(indexes);
                            }
                            else
                            {
                                indexes = tk.startOffsetToTerms[docsAndPositionsEnum.StartOffset];
                                Assert.IsNotNull(indexes);
                            }
                            if (terms.HasPositions)
                            {
                                bool foundPosition = false;
                                foreach (int index in indexes)
                                {
                                    if (tk.termBytes[index].Equals(termsEnum.Term) && tk.positions[index] == position)
                                    {
                                        foundPosition = true;
                                        break;
                                    }
                                }
                                Assert.IsTrue(foundPosition);
                            }
                            if (terms.HasOffsets)
                            {
                                bool foundOffset = false;
                                foreach (int index in indexes)
                                {
                                    if (tk.termBytes[index].Equals(termsEnum.Term) && tk.startOffsets[index] == docsAndPositionsEnum.StartOffset && tk.endOffsets[index] == docsAndPositionsEnum.EndOffset)
                                    {
                                        foundOffset = true;
                                        break;
                                    }
                                }
                                Assert.IsTrue(foundOffset);
                            }
                            if (terms.HasPayloads)
                            {
                                bool foundPayload = false;
                                foreach (int index in indexes)
                                {
                                    if (tk.termBytes[index].Equals(termsEnum.Term) && Equals(tk.payloads[index], docsAndPositionsEnum.GetPayload()))
                                    {
                                        foundPayload = true;
                                        break;
                                    }
                                }
                                Assert.IsTrue(foundPayload);
                            }
                        }
                        try
                        {
                            docsAndPositionsEnum.NextPosition();
                            Assert.Fail();
                        }
#pragma warning disable 168
                        catch (Exception e)
#pragma warning restore 168
                        {
                            // ok
                        }
                    }
                    Assert.AreEqual(DocsEnum.NO_MORE_DOCS, docsAndPositionsEnum.NextDoc());
                }
                this.docsAndPositionsEnum.Value = docsAndPositionsEnum;
            }
            Assert.IsNull(termsEnum.Next());
            for (int i = 0; i < 5; ++i)
            {
                if (Random.NextBoolean())
                {
                    Assert.IsTrue(termsEnum.SeekExact(RandomPicks.RandomFrom(Random, tk.termBytes)));
                }
                else
                {
                    Assert.AreEqual(SeekStatus.FOUND, termsEnum.SeekCeil(RandomPicks.RandomFrom(Random, tk.termBytes)));
                }
            }
        }

        protected internal virtual Document AddId(Document doc, string id)
        {
            doc.Add(new StringField("id", id, Field.Store.NO));
            return doc;
        }

        protected internal virtual int DocID(IndexReader reader, string id)
        {
            return (new IndexSearcher(reader)).Search(new TermQuery(new Term("id", id)), 1).ScoreDocs[0].Doc;
        }

        // [Test] // LUCENENET NOTE: For now, we are overriding this test in every subclass to pull it into the right context for the subclass
        // only one doc with vectors
        public virtual void TestRareVectors()
        {
            RandomDocumentFactory docFactory = new RandomDocumentFactory(this, 10, 20);
            foreach (Options options in ValidOptions())
            {
                int numDocs = AtLeast(200);
                int docWithVectors = Random.Next(numDocs);
                Document emptyDoc = new Document();
                Directory dir = NewDirectory();
                RandomIndexWriter writer = new RandomIndexWriter(Random, dir, ClassEnvRule.similarity, ClassEnvRule.timeZone);
                RandomDocument doc = docFactory.NewDocument(TestUtil.NextInt32(Random, 1, 3), 20, options);
                for (int i = 0; i < numDocs; ++i)
                {
                    if (i == docWithVectors)
                    {
                        writer.AddDocument(AddId(doc.ToDocument(), "42"));
                    }
                    else
                    {
                        writer.AddDocument(emptyDoc);
                    }
                }
                IndexReader reader = writer.Reader;
                int docWithVectorsID = DocID(reader, "42");
                for (int i = 0; i < 10; ++i)
                {
                    int docID = Random.Next(numDocs);
                    Fields fields = reader.GetTermVectors(docID);
                    if (docID == docWithVectorsID)
                    {
                        AssertEquals(doc, fields);
                    }
                    else
                    {
                        Assert.IsNull(fields);
                    }
                }
                Fields fields_ = reader.GetTermVectors(docWithVectorsID);
                AssertEquals(doc, fields_);
                reader.Dispose();
                writer.Dispose();
                dir.Dispose();
            }
        }

        // [Test] // LUCENENET NOTE: For now, we are overriding this test in every subclass to pull it into the right context for the subclass
        public virtual void TestHighFreqs()
        {
            RandomDocumentFactory docFactory = new RandomDocumentFactory(this, 3, 5);
            foreach (Options options in ValidOptions())
            {
                if (options == Options.NONE)
                {
                    continue;
                }
                using (Directory dir = NewDirectory())
                using (RandomIndexWriter writer = new RandomIndexWriter(Random, dir, ClassEnvRule.similarity, ClassEnvRule.timeZone))
                {
                    RandomDocument doc = docFactory.NewDocument(TestUtil.NextInt32(Random, 1, 2), AtLeast(20000),
                        options);
                    writer.AddDocument(doc.ToDocument());
                    using (IndexReader reader = writer.Reader)
                        AssertEquals(doc, reader.GetTermVectors(0));
                }
            }
        }

        // [Test] // LUCENENET NOTE: For now, we are overriding this test in every subclass to pull it into the right context for the subclass
        public virtual void TestLotsOfFields()
        {
            RandomDocumentFactory docFactory = new RandomDocumentFactory(this, 500, 10);
            foreach (Options options in ValidOptions())
            {
                Directory dir = NewDirectory();
                RandomIndexWriter writer = new RandomIndexWriter(Random, dir, ClassEnvRule.similarity, ClassEnvRule.timeZone);
                RandomDocument doc = docFactory.NewDocument(AtLeast(100), 5, options);
                writer.AddDocument(doc.ToDocument());
                IndexReader reader = writer.Reader;
                AssertEquals(doc, reader.GetTermVectors(0));
                reader.Dispose();
                writer.Dispose();
                dir.Dispose();
            }
        }

        // [Test] // LUCENENET NOTE: For now, we are overriding this test in every subclass to pull it into the right context for the subclass
        // different options for the same field
        public virtual void TestMixedOptions()
        {
            int numFields = TestUtil.NextInt32(Random, 1, 3);
            var docFactory = new RandomDocumentFactory(this, numFields, 10);
            foreach (var options1 in ValidOptions())
            {
                foreach (var options2 in ValidOptions())
                {
                    if (options1 == options2)
                    {
                        continue;
                    }
                    using (Directory dir = NewDirectory())
                    {
                        using (var writer = new RandomIndexWriter(Random, dir, ClassEnvRule.similarity, ClassEnvRule.timeZone))
                        {
                            RandomDocument doc1 = docFactory.NewDocument(numFields, 20, options1);
                            RandomDocument doc2 = docFactory.NewDocument(numFields, 20, options2);
                            writer.AddDocument(AddId(doc1.ToDocument(), "1"));
                            writer.AddDocument(AddId(doc2.ToDocument(), "2"));
                            using (IndexReader reader = writer.Reader)
                            {
                                int doc1ID = DocID(reader, "1");
                                AssertEquals(doc1, reader.GetTermVectors(doc1ID));
                                int doc2ID = DocID(reader, "2");
                                AssertEquals(doc2, reader.GetTermVectors(doc2ID));
                            }
                        }
                    }
                }
            }
        }

        // [Test] // LUCENENET NOTE: For now, we are overriding this test in every subclass to pull it into the right context for the subclass
        public virtual void TestRandom()
        {
            RandomDocumentFactory docFactory = new RandomDocumentFactory(this, 5, 20);
            int numDocs = AtLeast(100);
            RandomDocument[] docs = new RandomDocument[numDocs];
            for (int i = 0; i < numDocs; ++i)
            {
                docs[i] = docFactory.NewDocument(TestUtil.NextInt32(Random, 1, 3), TestUtil.NextInt32(Random, 10, 50), RandomOptions());
            }
            Directory dir = NewDirectory();
            RandomIndexWriter writer = new RandomIndexWriter(Random, dir, ClassEnvRule.similarity, ClassEnvRule.timeZone);
            for (int i = 0; i < numDocs; ++i)
            {
                writer.AddDocument(AddId(docs[i].ToDocument(), "" + i));
            }
            IndexReader reader = writer.Reader;
            for (int i = 0; i < numDocs; ++i)
            {
                int docID = DocID(reader, "" + i);
                AssertEquals(docs[i], reader.GetTermVectors(docID));
            }
            reader.Dispose();
            writer.Dispose();
            dir.Dispose();
        }

        // [Test] // LUCENENET NOTE: For now, we are overriding this test in every subclass to pull it into the right context for the subclass
        public virtual void TestMerge()
        {
            RandomDocumentFactory docFactory = new RandomDocumentFactory(this, 5, 20);
            int numDocs = AtLeast(100);
            int numDeletes = Random.Next(numDocs);
            HashSet<int?> deletes = new HashSet<int?>();
            while (deletes.Count < numDeletes)
            {
                deletes.Add(Random.Next(numDocs));
            }
            foreach (Options options in ValidOptions())
            {
                RandomDocument[] docs = new RandomDocument[numDocs];
                for (int i = 0; i < numDocs; ++i)
                {
                    docs[i] = docFactory.NewDocument(TestUtil.NextInt32(Random, 1, 3), AtLeast(10), options);
                }
                Directory dir = NewDirectory();
                RandomIndexWriter writer = new RandomIndexWriter(Random, dir, ClassEnvRule.similarity, ClassEnvRule.timeZone);
                for (int i = 0; i < numDocs; ++i)
                {
                    writer.AddDocument(AddId(docs[i].ToDocument(), "" + i));
                    if (Rarely())
                    {
                        writer.Commit();
                    }
                }
                foreach (int delete in deletes)
                {
                    writer.DeleteDocuments(new Term("id", "" + delete));
                }
                // merge with deletes
                writer.ForceMerge(1);
                IndexReader reader = writer.Reader;
                for (int i = 0; i < numDocs; ++i)
                {
                    if (!deletes.Contains(i))
                    {
                        int docID = DocID(reader, "" + i);
                        AssertEquals(docs[i], reader.GetTermVectors(docID));
                    }
                }
                reader.Dispose();
                writer.Dispose();
                dir.Dispose();
            }
        }

        // [Test] // LUCENENET NOTE: For now, we are overriding this test in every subclass to pull it into the right context for the subclass
        // run random tests from different threads to make sure the per-thread clones
        // don't share mutable data
        public virtual void TestClone()
        {
            RandomDocumentFactory docFactory = new RandomDocumentFactory(this, 5, 20);
            int numDocs = AtLeast(100);
            foreach (Options options in ValidOptions())
            {
                RandomDocument[] docs = new RandomDocument[numDocs];
                for (int i = 0; i < numDocs; ++i)
                {
                    docs[i] = docFactory.NewDocument(TestUtil.NextInt32(Random, 1, 3), AtLeast(10), options);
                }
                Directory dir = NewDirectory();
                RandomIndexWriter writer = new RandomIndexWriter(Random, dir, ClassEnvRule.similarity, ClassEnvRule.timeZone);
                for (int i = 0; i < numDocs; ++i)
                {
                    writer.AddDocument(AddId(docs[i].ToDocument(), "" + i));
                }
                IndexReader reader = writer.Reader;
                for (int i = 0; i < numDocs; ++i)
                {
                    int docID = DocID(reader, "" + i);
                    AssertEquals(docs[i], reader.GetTermVectors(docID));
                }

                AtomicObject<Exception> exception = new AtomicObject<Exception>();
                ThreadClass[] threads = new ThreadClass[2];
                for (int i = 0; i < threads.Length; ++i)
                {
                    threads[i] = new ThreadAnonymousInnerClassHelper(this, numDocs, docs, reader, exception, i);
                }
                foreach (ThreadClass thread in threads)
                {
                    thread.Start();
                }
                foreach (ThreadClass thread in threads)
                {
                    thread.Join();
                }
                reader.Dispose();
                writer.Dispose();
                dir.Dispose();
                Assert.IsNull(exception.Value, "One thread threw an exception");
            }
        }

        private class ThreadAnonymousInnerClassHelper : ThreadClass
        {
            private readonly BaseTermVectorsFormatTestCase outerInstance;

            private int numDocs;
            private RandomDocument[] docs;
            private IndexReader reader;
            private AtomicObject<Exception> exception;
            private int i;

            public ThreadAnonymousInnerClassHelper(BaseTermVectorsFormatTestCase outerInstance, int numDocs, RandomDocument[] docs, IndexReader reader, AtomicObject<Exception> exception, int i)
            {
                this.outerInstance = outerInstance;
                this.numDocs = numDocs;
                this.docs = docs;
                this.reader = reader;
                this.exception = exception;
                this.i = i;
            }

            public override void Run()
            {
                try
                {
                    for (int i = 0; i < AtLeast(100); ++i)
                    {
                        int idx = Random.Next(numDocs);
                        int docID = outerInstance.DocID(reader, "" + idx);
                        outerInstance.AssertEquals(docs[idx], reader.GetTermVectors(docID));
                    }
                }
                catch (Exception t)
                {
                    this.exception.Value = t;
                }
            }
        }
    }
}