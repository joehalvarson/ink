﻿using System.Text;

namespace Ink.Parsed
{
    internal class Choice : Parsed.Object, IWeavePoint, INamedContent
	{
        public ContentList startContent { get; protected set; }
        public ContentList choiceOnlyContent { get; protected set; }
        public ContentList innerContent { get; protected set; }

        public string name { get; set; }

        public Expression condition { 
            get { 
                return _condition; 
            } 
            set { 
                _condition = value; 
                if( _condition )
                    AddContent (_condition);
            }
        }

        public bool onceOnly { get; set; }
        public bool isInvisibleDefault { get; set; }

        public Divert terminatingDivert { get; private set; }
        public bool   hasExplicitGather {
            get {
                return terminatingDivert && terminatingDivert.isToGather;
            }
        }
        public bool hasTerminatingDivert {
            get {
                return terminatingDivert != null;
            }
        }
        public int    indentationDepth { get; set; }// = 1;
        public bool   hasWeaveStyleInlineBrackets { get; set; }

        // Required for IWeavePoint interface
        // Choice's target container. Used by weave to append any extra
        // nested weave content into.
        public Runtime.Container runtimeContainer { get { return _innerContentContainer; } }


        public override Runtime.Container containerForCounting {
            get {
                return _innerContentContainer;
            }
        }

        // Override runtimePath to point to the Choice's target content (after it's chosen),
        // as opposed to the default implementation which would point to the choice itself
        // (or it's outer container), which is what runtimeObject is.
        public override Runtime.Path runtimePath
        {
            get {
                return _innerContentContainer.path;
            }
        }

        public Choice (ContentList startContent, ContentList choiceOnlyContent, ContentList innerContent, Divert divert)
		{
            this.startContent = startContent;
            this.choiceOnlyContent = choiceOnlyContent;
            this.innerContent = innerContent;
			this.indentationDepth = 1;

            if (startContent)
                AddContent (this.startContent);

            if (choiceOnlyContent)
                AddContent (this.choiceOnlyContent);

            if( innerContent )
                AddContent (this.innerContent);

            this.onceOnly = true; // default

            if (divert) {
                terminatingDivert = divert;
                AddContent (terminatingDivert);
            }
		}


		public override Runtime.Object GenerateRuntimeObject ()
        {
            _outerContainer = new Runtime.Container ();

            // Content names for different types of choice:
            //  * start content [choice only content] inner content
            //  * start content   -> divert
            //  * start content
            //  * [choice only content]

            // Hmm, this structure has become slightly insane!
            //
            // [
            //     EvalStart
            //     BeginString
            //     PUSH (function)
            //     -> s
            //     EndString
            //     BeginString
            //     ... choice only content
            //     EndEval
            //     Condition expression
            //     choice: -> "c"
            //     (s) = [
            //         start content
            //     ]
            //     (c) = [
            //         PUSH (function)
            //         -> s
            //         inner content
            //     ]
            // ]

            _runtimeChoice = new Runtime.Choice (onceOnly);
            _runtimeChoice.isInvisibleDefault = this.isInvisibleDefault;

            if (startContent || choiceOnlyContent || condition) {
                _outerContainer.AddContent (Runtime.ControlCommand.EvalStart ());
            }

            // Start content is put into a named container that's referenced both
            // when displaying the choice initially, and when generating the text
            // when the choice is chosen.
            if (startContent) {

                // Mark the start of the choice text generation, so that the runtime
                // knows where to rewind to to extract the content from the output stream.
                _outerContainer.AddContent (Runtime.ControlCommand.BeginString ());

                // "Function call" to generate start content
                _divertToStartContentOuter = new Runtime.Divert (Runtime.PushPopType.Function);
                _outerContainer.AddContent (_divertToStartContentOuter);

                // Start content itself in a named container
                _startContentRuntimeContainer = startContent.GenerateRuntimeObject () as Runtime.Container;
                _startContentRuntimeContainer.name = "s";
                _outerContainer.AddToNamedContentOnly (_startContentRuntimeContainer);

                _outerContainer.AddContent (Runtime.ControlCommand.EndString ());

                _runtimeChoice.hasStartContent = true;
            }

            // Choice only content - mark the start, then generate it directly into the outer container
            if (choiceOnlyContent) {
                _outerContainer.AddContent (Runtime.ControlCommand.BeginString ());

                var choiceOnlyRuntimeContent = choiceOnlyContent.GenerateRuntimeObject () as Runtime.Container;
                _outerContainer.AddContentsOfContainer (choiceOnlyRuntimeContent);

                _outerContainer.AddContent (Runtime.ControlCommand.EndString ());

                _runtimeChoice.hasChoiceOnlyContent = true;
            }

            // Generate any condition for this choice
            if (condition) {
                condition.GenerateIntoContainer (_outerContainer);
                _runtimeChoice.hasCondition = true;
            }

            if (startContent || choiceOnlyContent || condition) {
                _outerContainer.AddContent (Runtime.ControlCommand.EvalEnd ());
            }
                
            // Add choice itself
            _outerContainer.AddContent (_runtimeChoice);

            // Container that choice points to for when it's chosen
            _innerContentContainer = new Runtime.Container ();

            // Repeat start content by diverting to its container
            if (startContent) {
                _divertToStartContentInner = new Runtime.Divert (Runtime.PushPopType.Function);
                _innerContentContainer.AddContent (_divertToStartContentInner);
            }

            // Choice's own inner content
            if (innerContent) {
				var innerChoiceOnlyContent = innerContent.GenerateRuntimeObject () as Runtime.Container;
                _innerContentContainer.AddContentsOfContainer (innerChoiceOnlyContent);
            }

            // Fully parsed choice will be a full line, so it needs to be terminated
            if (startContent || innerContent) {
                _innerContentContainer.AddContent(new Runtime.Text("\n"));
            }

            // Use "c" as the destination name within the choice's outer container
            _innerContentContainer.name = "c";
            _outerContainer.AddToNamedContentOnly (_innerContentContainer);

            if (this.story.countAllVisits) {
                _innerContentContainer.visitsShouldBeCounted = true;
                _innerContentContainer.turnIndexShouldBeCounted = true;
            }

            _innerContentContainer.countingAtStartOnly = true;

            // Does this choice end in an explicit divert?
            if (terminatingDivert) {
                _innerContentContainer.AddContent (terminatingDivert.runtimeObject);
            }

            return _outerContainer;
		}

        public override void ResolveReferences(Story context)
		{
			// Weave style choice - target own content container
            if (_innerContentContainer) {
                _runtimeChoice.pathOnChoice = _innerContentContainer.path;

                if (onceOnly)
                    _innerContentContainer.visitsShouldBeCounted = true;
            }

            if( _divertToStartContentOuter )
                _divertToStartContentOuter.targetPath = _startContentRuntimeContainer.path;

            if( _divertToStartContentInner )
                _divertToStartContentInner.targetPath = _startContentRuntimeContainer.path;

            base.ResolveReferences (context);
		}

        public override string ToString ()
        {
            if (choiceOnlyContent != null) {
                return string.Format ("* {0}[{1}]...", startContent, choiceOnlyContent);
            } else {
                return string.Format ("* {0}...", startContent);
            }
        }

        Runtime.Choice _runtimeChoice;
        Runtime.Container _innerContentContainer;
        Runtime.Container _outerContainer;
        Runtime.Container _startContentRuntimeContainer;
        Runtime.Divert _divertToStartContentOuter;
        Runtime.Divert _divertToStartContentInner;
        Expression _condition;
	}

}

