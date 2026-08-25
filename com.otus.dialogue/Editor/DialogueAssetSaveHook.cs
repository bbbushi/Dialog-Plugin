using UnityEditor;
using UnityEngine;

/// <summary>
/// 保存对话资产时做校验：有 Error 则写 Console（带 context，点击可选中资产）。
/// 不弹窗、不阻断保存——常驻提示交给 Inspector 徽标，钩子只保证"带伤保存"必留痕。
/// </summary>
public class DialogueAssetSaveHook : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        foreach (string path in paths)
        {
            if (!path.EndsWith(".asset"))
            {
                continue;
            }

            var asset = AssetDatabase.LoadAssetAtPath<DialogueAsset>(path);
            if (asset == null)
            {
                continue;
            }

            var issues = DialogueValidator.Validate(asset);
            if (!DialogueValidator.HasErrors(issues))
            {
                continue;
            }

            Debug.LogError($"[对话校验] 资产 \"{asset.name}\" 存在错误，本次保存的内容可能无法正常播放：", asset);
            foreach (var issue in issues)
            {
                if (issue.Severity == DialogueIssueSeverity.Error)
                {
                    Debug.LogError($"[对话校验] {asset.name}：{issue.Message}", asset);
                }
            }
        }

        return paths;
    }
}
