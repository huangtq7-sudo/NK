using System;
using System.Collections.Generic;
using System.Globalization;

namespace Naraka.Features.Dialogue.Model
{
    public enum DialogueSpeakerKind
    {
        Npc = 0,
        Player = 1
    }

    /// <summary>一行对白。显示长度按文本元素计，代理对与组合字符不会被逐字显示拆开。</summary>
    public sealed class DialogueLine
    {
        public DialogueLine(DialogueSpeakerKind speaker, string speakerName, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("Dialogue text cannot be empty.", nameof(text));
            }

            Speaker = speaker;
            SpeakerName = speakerName ?? string.Empty;
            Text = text;
            ElementCount = new StringInfo(text).LengthInTextElements;
        }

        public DialogueSpeakerKind Speaker { get; }

        public string SpeakerName { get; }

        public string Text { get; }

        public int ElementCount { get; }
    }

    /// <summary>一段按行序播放的对话。行序来自配置，不是随机对白。</summary>
    public sealed class DialogueScript
    {
        public DialogueScript(string dialogueId, IReadOnlyList<DialogueLine> lines)
        {
            if (string.IsNullOrEmpty(dialogueId))
            {
                throw new ArgumentException("DialogueId cannot be empty.", nameof(dialogueId));
            }

            if (lines == null || lines.Count == 0)
            {
                throw new ArgumentException("A dialogue needs at least one line.", nameof(lines));
            }

            DialogueId = dialogueId;
            Lines = lines;
        }

        public string DialogueId { get; }

        public IReadOnlyList<DialogueLine> Lines { get; }
    }

    public enum DialoguePhase
    {
        /// <summary>当前行正在逐字显示。</summary>
        Revealing = 0,

        /// <summary>当前行已经完整显示，等待点击继续。</summary>
        Revealed = 1,

        /// <summary>最后一行之后的一次点击已经结束对话。</summary>
        Finished = 2
    }

    public enum DialogueAdvanceResult
    {
        Ignored = 0,

        /// <summary>这次点击只把当前行全部显示，没有推进到下一行。</summary>
        RevealedLine = 1,

        NextLine = 2,

        Finished = 3
    }

    /// <summary>
    /// 对话的揭示状态。纯逻辑、零引擎依赖，可在 EditMode 逐帧断言。
    ///
    /// 一次点击只做一件事：逐字中点击揭示本行，已揭示时点击推进下一行，
    /// 最后一行已揭示时点击结束。因此不可能出现"一次点击既揭示又跳过下一句"。
    /// 逐字速度是表现参数，不影响任何业务时间。
    /// </summary>
    public sealed class DialogueSession
    {
        private double _revealAccumulator;

        public DialogueSession(DialogueScript script)
        {
            Script = script ?? throw new ArgumentNullException(nameof(script));
            Phase = DialoguePhase.Revealing;
        }

        public DialogueScript Script { get; }

        public int LineIndex { get; private set; }

        /// <summary>当前行已显示的文本元素数。</summary>
        public int VisibleElements { get; private set; }

        public DialoguePhase Phase { get; private set; }

        public DialogueLine CurrentLine => Script.Lines[LineIndex];

        public bool IsLastLine => LineIndex == Script.Lines.Count - 1;

        /// <summary>推进逐字显示。速度非正时立即显示整行。返回可见数量是否变化。</summary>
        public bool Tick(float deltaSeconds, float elementsPerSecond)
        {
            if (Phase != DialoguePhase.Revealing)
            {
                return false;
            }

            var total = CurrentLine.ElementCount;
            if (elementsPerSecond <= 0f || float.IsNaN(elementsPerSecond) || float.IsInfinity(elementsPerSecond))
            {
                return RevealCurrentLine();
            }

            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds))
            {
                return false;
            }

            _revealAccumulator += deltaSeconds * elementsPerSecond;
            var whole = (int)Math.Floor(_revealAccumulator);
            if (whole <= 0)
            {
                return false;
            }

            _revealAccumulator -= whole;
            var next = Math.Min(total, VisibleElements + whole);
            var changed = next != VisibleElements;
            VisibleElements = next;
            if (VisibleElements >= total)
            {
                Phase = DialoguePhase.Revealed;
                _revealAccumulator = 0d;
            }

            return changed;
        }

        public DialogueAdvanceResult Advance()
        {
            switch (Phase)
            {
                case DialoguePhase.Revealing:
                    RevealCurrentLine();
                    return DialogueAdvanceResult.RevealedLine;

                case DialoguePhase.Revealed when !IsLastLine:
                    LineIndex++;
                    VisibleElements = 0;
                    _revealAccumulator = 0d;
                    Phase = DialoguePhase.Revealing;
                    return DialogueAdvanceResult.NextLine;

                case DialoguePhase.Revealed:
                    Phase = DialoguePhase.Finished;
                    return DialogueAdvanceResult.Finished;

                default:
                    return DialogueAdvanceResult.Ignored;
            }
        }

        private bool RevealCurrentLine()
        {
            var changed = VisibleElements != CurrentLine.ElementCount;
            VisibleElements = CurrentLine.ElementCount;
            Phase = DialoguePhase.Revealed;
            _revealAccumulator = 0d;
            return changed;
        }
    }
}
