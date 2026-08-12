/**
 * Bilingual dictionary of helper labels for the Chat page.
 * Detects the answer language (Chinese/English) and dynamically switches all helper UI text.
 * Stays lightweight by avoiding the Angular i18n framework.
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
 * Heuristic language detection: if CJK characters exceed 20%, classify as Chinese.
 * Only used for helper UI; a misdetection does not affect core functionality.
 */
export function detectChatLang(text: string): ChatLang {
  if (!text) return 'zh';
  const cjkCount = (text.match(/[\u4e00-\u9fff\u3400-\u4dbf]/g) ?? []).length;
  return cjkCount / text.length > 0.2 ? 'zh' : 'en';
}
