import { useState, type FormEvent } from 'react';
import { createIssue, toUserMessage, triageIssue } from '../api';
import type { Issue, IssuePriority, TriageResponse } from '../types';
import { PRIORITY_LABELS, PRIORITY_OPTIONS } from '../types';
import TriageReport from './TriageReport';

export interface NewIssuePanelProps {
  /** 提交成功后把新问题交给上层（会立刻出现在列表里）。 */
  onCreated: (issue: Issue) => void;
  /** 定位到某个已有问题（相似问题跳转）。 */
  onLocateIssue: (id: string) => void;
}

const emptyForm = {
  title: '',
  description: '',
  priority: 'medium' as IssuePriority,
  x: '0',
  y: '0',
  z: '0',
};

/**
 * 上报面板：巡检员没带手机时，管理端也能手动补录一条问题。
 * 「智能分诊」是本地算法（关键词 + 相似度 + 距离），不会把数据发到外部。
 */
export default function NewIssuePanel({ onCreated, onLocateIssue }: NewIssuePanelProps) {
  const [form, setForm] = useState(emptyForm);
  const [triage, setTriage] = useState<TriageResponse | null>(null);
  const [triageError, setTriageError] = useState<string | null>(null);
  const [triaging, setTriaging] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const update = (key: keyof typeof emptyForm) => (value: string) => {
    setForm((current) => ({ ...current, [key]: value }));
  };

  const handleTriage = async () => {
    if (form.title.trim().length === 0) {
      setTriageError('请先填写标题再做智能分诊');
      return;
    }
    setTriaging(true);
    setTriageError(null);
    try {
      const result = await triageIssue({
        title: form.title.trim(),
        description: form.description.trim(),
        position: {
          x: Number(form.x) || 0,
          y: Number(form.y) || 0,
          z: Number(form.z) || 0,
        },
      });
      setTriage(result);
    } catch (error) {
      setTriageError(`智能分诊失败：${toUserMessage(error)}`);
    } finally {
      setTriaging(false);
    }
  };

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (form.title.trim().length === 0) {
      setSubmitError('标题不能为空');
      return;
    }
    setSubmitting(true);
    setSubmitError(null);
    try {
      const issue = await createIssue({
        title: form.title.trim(),
        description: form.description.trim(),
        priority: form.priority,
        position: {
          x: Number(form.x) || 0,
          y: Number(form.y) || 0,
          z: Number(form.z) || 0,
        },
      });
      onCreated(issue);
      setForm(emptyForm);
      setTriage(null);
    } catch (error) {
      setSubmitError(`提交失败：${toUserMessage(error)}`);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section className="panel">
      <div className="panel__head">
        <h2 className="panel__title">补录问题</h2>
        <span className="panel__hint">巡检员未带设备时使用</span>
      </div>

      <form className="form" onSubmit={handleSubmit}>
        <label className="field">
          <span className="field__label">标题</span>
          <input
            className="field__input"
            name="title"
            value={form.title}
            placeholder="例如：东侧外墙渗水"
            onChange={(event) => update('title')(event.target.value)}
          />
        </label>

        <label className="field">
          <span className="field__label">描述</span>
          <textarea
            className="field__input field__input--area"
            name="description"
            value={form.description}
            placeholder="墙皮发黑，有水渍往下淌"
            onChange={(event) => update('description')(event.target.value)}
          />
        </label>

        <div className="field-row">
          <label className="field field--tight">
            <span className="field__label">优先级</span>
            <select
              className="field__input"
              name="priority"
              value={form.priority}
              onChange={(event) => update('priority')(event.target.value)}
            >
              {PRIORITY_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {PRIORITY_LABELS[option]}
                </option>
              ))}
            </select>
          </label>

          <label className="field field--tight">
            <span className="field__label">X</span>
            <input
              className="field__input"
              name="x"
              value={form.x}
              onChange={(event) => update('x')(event.target.value)}
            />
          </label>
          <label className="field field--tight">
            <span className="field__label">Y</span>
            <input
              className="field__input"
              name="y"
              value={form.y}
              onChange={(event) => update('y')(event.target.value)}
            />
          </label>
          <label className="field field--tight">
            <span className="field__label">Z</span>
            <input
              className="field__input"
              name="z"
              value={form.z}
              onChange={(event) => update('z')(event.target.value)}
            />
          </label>
        </div>

        <div className="form__actions">
          <button
            type="button"
            className="button button--outline"
            onClick={() => {
              void handleTriage();
            }}
            disabled={triaging}
          >
            {triaging ? '分诊中…' : '智能分诊'}
          </button>
          <button type="submit" className="button button--solid" disabled={submitting}>
            {submitting ? '提交中…' : '提交问题'}
          </button>
        </div>

        {triageError ? (
          <p className="form__error" role="alert">
            {triageError}
          </p>
        ) : null}
        {submitError ? (
          <p className="form__error" role="alert">
            {submitError}
          </p>
        ) : null}
      </form>

      {triage ? (
        <TriageReport
          triage={triage}
          currentPriority={form.priority}
          onAdoptPriority={(priority) => {
            setForm((current) => ({ ...current, priority }));
          }}
          onLocateIssue={onLocateIssue}
        />
      ) : null}
    </section>
  );
}