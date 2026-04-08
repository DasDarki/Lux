local function __lux_concat(a, b) return tostring(a) .. tostring(b) end

function add(a, b)
	return a + b
end
local result = add(1, 2)
print("Result: " .. result)
local arr = {
	10,
	20,
	30
}
local sum = 0
for i = 0, 2 do
	sum = sum + arr[(i + 1)]
end
print(sum)
function greet(name, greeting)
	return __lux_concat(__lux_concat(greeting, ", " .. name), "!")
end
local msg = greet("World", "Hello")
print(msg)
local len = #arr
print(len)
local strInterp = "The sum of 1 and 2 is " .. tostring(result)
print(strInterp)
local nilTest = nil
greet(nilTest, "Hi")
return {
	add = add,
	greet = greet
}
