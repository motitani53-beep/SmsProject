// ולידציה למספר טלפון ישראלי – רק ספרות, פורמט 972 + 9 ספרות (12 ספרות)

/** פורמט תקין: מתחיל ב-972 ואחריו בדיוק 9 ספרות */
export const PHONE_REGEX = /^972\d{9}$/;

/** המחרוזת מכילה רק ספרות (0-9) */
const ONLY_DIGITS = /^\d+$/;

export interface PhoneValidationResult {
  isValid: boolean;
  error?: string;
  normalizedPhone?: string;
}

export function validatePhone(phone: string): PhoneValidationResult {
  const trimmed = phone.trim();

  if (!trimmed) {
    return {
      isValid: false,
      error: 'מספר טלפון ריק',
    };
  }

  // חייב להכיל רק ספרות – אין תווים באנגלית, רווחים, מקפים וכו'
  if (!ONLY_DIGITS.test(trimmed)) {
    return {
      isValid: false,
      error: 'מכיל תווים לא חוקיים – רק ספרות מותרות',
    };
  }

  if (trimmed.length !== 12) {
    if (trimmed.length < 12) {
      return {
        isValid: false,
        error: 'מספר הטלפון קצר מדי (נדרשות 12 ספרות)',
      };
    }
    return {
      isValid: false,
      error: 'מספר הטלפון ארוך מדי (מקסימום 12 ספרות)',
    };
  }

  // חייב להתחיל ב-972 (קידומת ישראל)
  if (!PHONE_REGEX.test(trimmed)) {
    return {
      isValid: false,
      error: 'מספר ישראלי חייב להתחיל ב-972 (12 ספרות)',
    };
  }

  return {
    isValid: true,
    normalizedPhone: trimmed,
  };
}

export function formatPhoneDisplay(phone: string): string {
  if (phone.length !== 12) return phone;
  return `${phone.slice(0, 3)}-${phone.slice(3, 5)}-${phone.slice(5, 8)}-${phone.slice(8)}`;
}
