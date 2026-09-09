namespace SephPlanner.DataTool;

public static class CommandLine
{
    public const string Help = """
        SephPlanner 진단 도구
          [게임 폴더]                              로컬 텍스트 추출
          --check <스냅샷.json>                     현재 로컬 카탈로그로 레벨 대조
          --replay <녹화 폴더>                      기본 설정으로 기존 스냅샷 재생
          --churn <스냅샷.json> [양수 반복 횟수]      반복 적용 시뮬레이션
          --reproduce <파일.replay>                 저장된 설정·카탈로그·직전 목표로 계획 재현
          --reproduce <파일.replay> --allow-model-change
                                                   다른 계산 빌드로 변경 전후 비교
          --solve                                  합성 배치 검사
          --prediction-probe                       효과 갱신 순서의 합성 반례 검사
          --measure                                콤보 가치 환산
          --values                                 가치 평가 현황과 초안
          --help                                   이 도움말

        한 번에 명령 하나만 실행합니다.
        --reproduce 종료 코드: 0=비교 항목 일치, 1=입력·실행 실패, 2=저장 결과와 다름.
        재현 파일은 게임 데이터가 포함된 로컬 진단 자료입니다. 배포물이나 저장소에 넣지 않습니다.
        """;

    public static string? Error(string[] args)
    {
        if (args.Length == 0) return null;
        if (!args[0].StartsWith('-'))
            return args.Length == 1 ? null : "게임 폴더 또는 명령 하나만 지정하세요.";
        var valid = args[0] switch
        {
            "--help" or "-h" or "--solve" or "--prediction-probe" or "--measure" or "--values" => args.Length == 1,
            "--check" or "--replay" => args.Length == 2 && PathArgument(args[1]),
            "--churn" => args.Length is 2 or 3 && PathArgument(args[1]) &&
                         (args.Length == 2 || int.TryParse(args[2], out var rounds) && rounds > 0),
            "--reproduce" => args.Length is 2 or 3 && PathArgument(args[1]) &&
                             (args.Length == 2 || args[2] == "--allow-model-change"),
            _ => false,
        };
        return valid ? null : "알 수 없는 명령이거나 인자가 올바르지 않습니다. --help로 사용법을 확인하세요.";
    }

    private static bool PathArgument(string value) => !string.IsNullOrWhiteSpace(value) && !value.StartsWith('-');
}
