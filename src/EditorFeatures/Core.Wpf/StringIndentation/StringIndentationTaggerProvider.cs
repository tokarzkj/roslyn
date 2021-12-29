﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Implementation.Tagging;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Options;
using Microsoft.CodeAnalysis.Editor.Shared.Tagging;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Editor.Tagging;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.StringIndentation;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Text.Shared.Extensions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.StringIndentation
{
    /// <summary>
    /// This factory is called to create taggers that provide information about how strings are indented.
    /// </summary>
    [Export(typeof(ITaggerProvider))]
    [TagType(typeof(StringIndentationTag))]
    [VisualStudio.Utilities.ContentType(ContentTypeNames.CSharpContentType)]
    [VisualStudio.Utilities.ContentType(ContentTypeNames.VisualBasicContentType)]
    internal partial class StringIndentationTaggerProvider : AsynchronousTaggerProvider<StringIndentationTag>
    {
        private readonly IEditorFormatMap _editorFormatMap;

        protected override IEnumerable<PerLanguageOption2<bool>> PerLanguageOptions => SpecializedCollections.SingletonEnumerable(FeatureOnOffOptions.StringIdentation);

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public StringIndentationTaggerProvider(
            IThreadingContext threadingContext,
            IEditorFormatMapService editorFormatMapService,
            IGlobalOptionService globalOptions,
            IAsynchronousOperationListenerProvider listenerProvider)
            : base(threadingContext, globalOptions, listenerProvider.GetListener(FeatureAttribute.StringIndentation))
        {
            _editorFormatMap = editorFormatMapService.GetEditorFormatMap("text");
        }

        protected override TaggerDelay EventChangeDelay => TaggerDelay.NearImmediate;

        protected override ITaggerEventSource CreateEventSource(
            ITextView textView, ITextBuffer subjectBuffer)
        {
            return TaggerEventSources.Compose(
                new EditorFormatMapChangedEventSource(_editorFormatMap),
                TaggerEventSources.OnTextChanged(subjectBuffer));
        }

        protected override async Task ProduceTagsAsync(
            TaggerContext<StringIndentationTag> context, DocumentSnapshotSpan documentSnapshotSpan, int? caretPosition, CancellationToken cancellationToken)
        {
            var document = documentSnapshotSpan.Document;
            if (document == null)
                return;

            if (!GlobalOptions.GetOption(FeatureOnOffOptions.StringIdentation, document.Project.Language))
                return;

            var service = document.GetLanguageService<IStringIndentationService>();
            if (service == null)
                return;

            var snapshotSpan = documentSnapshotSpan.SnapshotSpan;
            var regions = await service.GetStringIndentationRegionsAsync(document, snapshotSpan.Span.ToTextSpan(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (regions.Length == 0)
                return;

            var snapshot = snapshotSpan.Snapshot;
            foreach (var region in regions)
            {
                var line = snapshot.GetLineFromPosition(region.IndentSpan.End);

                // If the indent is on the first column, then no need to actually show anything (plus we can't as we
                // want to draw one column earlier, and that column doesn't exist).
                if (line.Start == region.IndentSpan.End)
                    continue;

                context.AddTag(new TagSpan<StringIndentationTag>(
                    region.IndentSpan.ToSnapshotSpan(snapshot),
                    new StringIndentationTag(
                        _editorFormatMap,
                        GetHoleSpans(snapshot, region))));
            }
        }

        private static ImmutableArray<SnapshotSpan> GetHoleSpans(ITextSnapshot snapshot, StringIndentationRegion region)
        {
            using var _ = ArrayBuilder<SnapshotSpan>.GetInstance(out var result);

            foreach (var hole in region.OrderedHoleSpans)
            {
                if (!IgnoreHole(snapshot, region, hole))
                    result.Add(hole.ToSnapshotSpan(snapshot));
            }

            return result.ToImmutable();
        }

        private static bool IgnoreHole(ITextSnapshot snapshot, StringIndentationRegion region, TextSpan hole)
        {
            // We can ignore the hole if all the content of it is after the region's indentation level.
            // In that case, it's fine to draw the line through the hole as it won't intersect any code
            // (or show up on the right side of the line).
            var lastLine = snapshot.GetLineFromPosition(region.IndentSpan.End);
            var offsetOpt = lastLine.GetFirstNonWhitespaceOffset();
            Contract.ThrowIfNull(offsetOpt);

            var holeStartLine = snapshot.GetLineFromPosition(hole.Start).LineNumber;
            var holeEndLine = snapshot.GetLineFromPosition(hole.End).LineNumber;

            for (var i = holeStartLine; i <= holeEndLine; i++)
            {
                var line = snapshot.GetLineFromLineNumber(i);
                var currentLineOffset = line.GetFirstNonWhitespaceOffset();

                if (currentLineOffset != null && currentLineOffset < offsetOpt)
                    return false;
            }

            return true;
        }
    }
}
