using System;
using System.Collections.Generic;

namespace SephPlanner.Core.Extensions
{
    /// <summary>
    /// 확장이 자기 설정을 F3 설정 창의 "확장 모드" 묶음에 내놓는 통로. <b>실험적이다.</b>
    /// <see cref="IPlanExtension"/>을 구현한 같은 객체에 붙이며, 구현하지 않은 확장은 묶음에 나오지 않는다.
    ///
    /// 창은 줄을 그리기만 하고 값의 뜻은 모른다. 저장도 확장이 한다. 줄 하나를 바꿀 때마다
    /// <see cref="Options"/>를 다시 불러 묶음을 새로 그리므로, 꺼져 있을 때 켜기 줄만 돌려주면 아래 줄이 접힌다.
    /// 확장이 주는 글자는 번역하지 않는다 - 화면 언어는 <see cref="Text.Localization.Current"/>로 읽어 맞춘다.
    /// </summary>
    public interface IPlanExtensionOptions
    {
        /// <summary>묶음 안에서 이 확장의 줄들 위에 붙는 이름.</summary>
        string Title { get; }

        /// <summary>
        /// 그릴 줄들. 메인 스레드에서 창을 짓거나 줄 하나가 바뀔 때마다 불린다. 예외를 던지면 이 확장은
        /// 새 판이 시작될 때까지 꺼진다(<see cref="IPlanExtension.Values"/>와 같다).
        /// </summary>
        IReadOnlyList<ExtensionOption> Options();
    }

    /// <summary>F3 의 <c>&lt; 값 &gt;</c> 줄 하나. 고를 것이 정해진 값 하나다.</summary>
    public sealed class ExtensionOption
    {
        public string Label { get; set; } = "";

        /// <summary>화살표로 넘기는 선택지. 비어 있으면 그 줄은 그리지 않는다.</summary>
        public string[] Choices { get; set; } = Array.Empty<string>();

        /// <summary>지금 고른 선택지의 번호. 범위를 벗어나면 양끝으로 맞춰 보여 준다.</summary>
        public Func<int> Read { get; set; } = () => 0;

        /// <summary>고른 번호를 받아 저장한다.</summary>
        public Action<int> Write { get; set; } = _ => { };
    }
}
