import { useEffect, useState } from "react"

/**
 * 타이핑이 멈춘 뒤에야 값을 흘려보낸다.
 * 검색창이 글자마다 Core 를 때리면 그 뒤의 Tidal 질의가 한도(429)를 부른다.
 */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value)

  useEffect(() => {
    const id = setTimeout(() => setSettled(value), delayMs)
    return () => clearTimeout(id)
  }, [value, delayMs])

  return settled
}
