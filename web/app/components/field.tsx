import { useId, type ComponentProps, type ReactNode } from "react";

import { cx, focusRing, inputBase } from "./styles";

export type FieldControlProps = {
  id: string;
  "aria-invalid": true | undefined;
  "aria-describedby": string | undefined;
};

type FieldProps = {
  label: ReactNode;
  error?: string;
  className?: string;
  // Render-prop: spread the received props onto the control, e.g.
  // <Field label="Login">{(p) => <Input name="userName" {...p} />}</Field>
  children: (control: FieldControlProps) => ReactNode;
};

export function Field({ label, error, className, children }: FieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  return (
    <div className={cx("space-y-1", className)}>
      <label htmlFor={id} className="block text-sm font-medium text-text">
        {label}
      </label>
      {children({
        id,
        "aria-invalid": error ? true : undefined,
        "aria-describedby": error ? errorId : undefined,
      })}
      {error && (
        <p id={errorId} role="alert" className="text-sm text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}

type InputProps = ComponentProps<"input"> & { mono?: boolean };

export function Input({ mono, className, ...props }: InputProps) {
  return <input className={cx(inputBase, mono && "font-mono", focusRing, className)} {...props} />;
}
