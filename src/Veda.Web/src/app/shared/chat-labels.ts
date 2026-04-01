/**
 * Chat 页辅助标签的双语字典。
 * 根据回答内容检测语言（中文/英文），动态切换所有辅助 UI 文字。
 * 不引入 Angular i18n 框架，保持轻量。
 */

export type ChatLang = 'zh' | 'en';

export interface ChatLabels {
  sources: (n: number) => string;
  hallucination: string;
  feedbackQuestion: string;
  helpful: string;
  notHelpful: string;
}

export const CHAT_LABELS: Record<ChatLang, ChatLabels> = {
  zh: {
    sources:         (n) => `📎 参考来源（${n} 处）`,
    hallucination:   '⚠ 可能存在幻觉',
    feedbackQuestion: '这个回答有帮助吗？',
    helpful:         '👍 有用',
    notHelpful:      '👎 没帮助',
  },
  en: {
    sources:         (n) => `📎 Sources (${n})`,
    hallucination:   '⚠ Possible hallucination',
    feedbackQuestion: 'Was this answer helpful?',
    helpful:         '👍 Helpful',
    notHelpful:      '👎 Not helpful',
  },
};

/**
 * 启发式语言检测：CJK 字符占比 > 20% 判定为中文。
 * 仅用于辅助 UI，误判不影响核心功能。
 */
export function detectChatLang(text: string): ChatLang {
  if (!text) return 'zh';
  const cjkCount = (text.match(/[\u4e00-\u9fff\u3400-\u4dbf]/g) ?? []).length;
  return cjkCount / text.length > 0.2 ? 'zh' : 'en';
}
